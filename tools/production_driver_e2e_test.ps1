[CmdletBinding()]
param(
    [string]$Log = '',
    [string]$TestTarget = 'C:\Windows\System32\notepad.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'Games8thBlocker.exe'
$lines = New-Object System.Collections.Generic.List[string]
$exitCode = 1

if ([string]::IsNullOrWhiteSpace($Log)) {
    $Log = Join-Path $root 'production_driver_e2e_20260926.log'
}

function Add-Result([string]$text) {
    $entry = '[{0:HH:mm:ss.fff}] {1}' -f (Get-Date), $text
    [void]$lines.Add($entry)
    Write-Host $text
}

function Invoke-Captured([string]$file, [string[]]$arguments) {
    Add-Result ('> ' + $file + ' ' + ($arguments -join ' '))
    $output = & $file @arguments 2>&1
    $code = $LASTEXITCODE
    foreach ($item in $output) { Add-Result ([string]$item) }
    Add-Result ('EXIT=' + $code)
    return $code
}

try {
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    $elevated = $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    Add-Result ('ELEVATED=' + $elevated)
    if (-not $elevated) { throw 'ADMIN_REQUIRED' }

    if (-not (Test-Path -LiteralPath $TestTarget)) {
        throw "TEST_TARGET_NOT_FOUND=$TestTarget"
    }
    Add-Result 'LOAD_MODE=EMBEDDED_DRIVER_RESOURCE'
    $testCode = Invoke-Captured $app @('cli', 'test', $TestTarget)
    if ($testCode -ne 0) { throw "DIRECT_TEST_FAILED=$testCode" }

    $statusCode = Invoke-Captured $app @('cli', 'driver-status')
    if ($statusCode -ne 0) { throw "STATUS_FAILED=$statusCode" }

    $listCode = Invoke-Captured $app @('cli', 'list')
    if ($listCode -ne 0) { throw "LIST_FAILED=$listCode" }

    $clearCode = Invoke-Captured $app @('cli', 'clear')
    if ($clearCode -ne 0) { throw "CLEAR_FAILED=$clearCode" }

    Add-Result 'PRODUCTION_CHAIN_VERIFIED=True'
    $exitCode = 0
}
catch {
    Add-Result ('EXCEPTION=' + $_.Exception.Message)
    $exitCode = 1
}
finally {
    try {
        $unloadCode = Invoke-Captured $app @('cli', 'unload')
        if ($unloadCode -ne 0 -and $exitCode -eq 0) { $exitCode = 2 }
    } catch {
        Add-Result ('UNLOAD_EXCEPTION=' + $_.Exception.Message)
        if ($exitCode -eq 0) { $exitCode = 3 }
    }
    Add-Result ('FINAL_EXIT=' + $exitCode)
    [IO.File]::WriteAllLines($Log, $lines, [Text.UTF8Encoding]::new($false))
}

exit $exitCode
