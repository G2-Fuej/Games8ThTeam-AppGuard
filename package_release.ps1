[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
$release = [IO.Path]::GetFullPath((Join-Path $root 'release'))
$appSource = Join-Path $root 'Games8thBlocker.exe'
$driverSource = Join-Path $root 'driver\build\Release\Games8thGuard.sys'
$appDestination = Join-Path $release 'Games8Th.Team-Feilian-CLI.exe'
$hashDestination = $appDestination + '.sha256.txt'
$driverResourceName = 'Games8thTeamBlocker.Games8thGuard.sys'
$approvedDriverHash = 'CFCB98EC34428375E8374721DBBF9B588CB22E93B652F01B9BCF23A531297C97'

if (-not $release.StartsWith($root.TrimEnd('\') + '\',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write outside the repository: $release"
}

if (-not $SkipBuild) {
    & cmd.exe /d /c 'build.bat <nul'
    if ($LASTEXITCODE -ne 0) {
        throw "Single-EXE build failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath $appSource)) {
    throw 'Games8thBlocker.exe was not produced'
}
if (-not (Test-Path -LiteralPath $driverSource)) {
    throw 'Signed Games8thGuard.sys is missing'
}

$appSignature = Get-AuthenticodeSignature -LiteralPath $appSource
if ($appSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Games8thBlocker.exe Authenticode status is $($appSignature.Status); sign the final EXE and rerun with -SkipBuild"
}

$driverSignature = Get-AuthenticodeSignature -LiteralPath $driverSource
if ($driverSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Games8thGuard.sys Authenticode status is $($driverSignature.Status)"
}
$driverHash = (Get-FileHash -LiteralPath $driverSource -Algorithm SHA256).Hash
if ($driverHash -ne $approvedDriverHash) {
    throw "Games8thGuard.sys SHA-256 is not the approved signed artifact: $driverHash"
}

New-Item -ItemType Directory -Path $release -Force | Out-Null
Copy-Item -LiteralPath $appSource -Destination $appDestination -Force
$sourceHash = (Get-FileHash -LiteralPath $appSource -Algorithm SHA256).Hash
$destinationHash = (Get-FileHash -LiteralPath $appDestination -Algorithm SHA256).Hash
if ($sourceHash -ne $destinationHash) {
    throw "Single EXE copy verification failed: source=$sourceHash destination=$destinationHash"
}
$destinationSignature = Get-AuthenticodeSignature -LiteralPath $appDestination
if ($destinationSignature.Status -ne
    [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Packaged EXE Authenticode status is $($destinationSignature.Status)"
}

$assembly = [Reflection.Assembly]::LoadFile($appDestination)
$resourceNames = $assembly.GetManifestResourceNames()
if ($resourceNames -notcontains $driverResourceName) {
    throw "Single EXE does not contain $driverResourceName"
}
$stream = $assembly.GetManifestResourceStream($driverResourceName)
if ($null -eq $stream) { throw 'Embedded driver resource cannot be opened' }
try {
    $memory = New-Object IO.MemoryStream
    try {
        $stream.CopyTo($memory)
        $embeddedDriver = $memory.ToArray()
    }
    finally { $memory.Dispose() }
}
finally { $stream.Dispose() }

$sha = [Security.Cryptography.SHA256]::Create()
try {
    $embeddedHash = ([BitConverter]::ToString(
        $sha.ComputeHash($embeddedDriver))).Replace('-', '')
}
finally { $sha.Dispose() }
if ($embeddedHash -ne $approvedDriverHash) {
    throw "Embedded driver SHA-256 mismatch: $embeddedHash"
}

$verificationDriver = Join-Path $release '.embedded-driver-verification.sys'
try {
    [IO.File]::WriteAllBytes($verificationDriver, $embeddedDriver)
    $embeddedSignature = Get-AuthenticodeSignature -LiteralPath $verificationDriver
    if ($embeddedSignature.Status -ne
        [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Embedded driver Authenticode status is $($embeddedSignature.Status)"
    }
}
finally {
    if (Test-Path -LiteralPath $verificationDriver) {
        [IO.File]::Delete($verificationDriver)
    }
}

[IO.File]::WriteAllText(
    $hashDestination,
    ($destinationHash.ToLowerInvariant() + '  Games8Th.Team-Feilian-CLI.exe' + [char]10),
    [Text.ASCIIEncoding]::new())

# Remove previous split-package outputs. Runtime delivery is now one EXE.
$legacyDirectory = Join-Path $release 'Games8thBlocker-portable'
if (Test-Path -LiteralPath $legacyDirectory) {
    [IO.Directory]::Delete($legacyDirectory, $true)
}
foreach ($legacyFile in @(
    'Games8thBlocker-portable.zip',
    'Games8thBlocker-portable.zip.sha256.txt')) {
    $legacyPath = Join-Path $release $legacyFile
    if (Test-Path -LiteralPath $legacyPath) { [IO.File]::Delete($legacyPath) }
}

Write-Output 'SINGLE_EXE=True'
Write-Output ('EXE_SHA256=' + $destinationHash.ToLowerInvariant())
Write-Output ('OUTER_EXE_SIGNATURE=' + $destinationSignature.Status)
Write-Output ('EMBEDDED_DRIVER_SHA256=' + $embeddedHash.ToLowerInvariant())
Write-Output ('EMBEDDED_DRIVER_SIGNATURE=' + $embeddedSignature.Status)
Get-Item -LiteralPath $appDestination, $hashDestination |
    Select-Object FullName, Length
