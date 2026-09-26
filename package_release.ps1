[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
$stage = [IO.Path]::GetFullPath((Join-Path $root 'release\Games8thBlocker-portable'))
$rootPrefix = $root.TrimEnd('\') + '\'

if (-not $stage.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write outside the repository: $stage"
}

if (-not $SkipBuild) {
    & cmd.exe /d /c 'build.bat <nul'
    if ($LASTEXITCODE -ne 0) {
        throw "User-mode build failed with exit code $LASTEXITCODE"
    }
}

$appSource = Join-Path $root 'Games8thBlocker.exe'
if (-not (Test-Path -LiteralPath $appSource)) {
    throw 'Games8thBlocker.exe was not produced'
}
$driverSource = Join-Path $root 'driver\build\Release\Games8thGuard.sys'
if (-not (Test-Path -LiteralPath $driverSource)) {
    throw 'Games8thGuard.sys was not produced; refusing to create a driver-only package'
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'assets') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'driver') -Force | Out-Null

$portableAppName = 'Games8Th.Team-Feilian-CLI.exe'
$appDestination = Join-Path $stage $portableAppName
Copy-Item -LiteralPath $appSource -Destination $appDestination -Force
$sourceHash = (Get-FileHash -LiteralPath $appSource -Algorithm SHA256).Hash
$destinationHash = (Get-FileHash -LiteralPath $appDestination -Algorithm SHA256).Hash
if ($sourceHash -ne $destinationHash) {
    throw "Games8thBlocker.exe copy verification failed: source=$sourceHash destination=$destinationHash"
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'verification_2026-09-25.md') -Destination $stage

foreach ($asset in @('mark_64.png', 'logo_mark.png', 'app.ico')) {
    $source = Join-Path $root "assets\$asset"
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage "assets\$asset")
    }
}

foreach ($file in @('load.bat', 'unload.bat')) {
    $source = Join-Path $root "driver\$file"
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage "driver\$file")
    }
}

Copy-Item -LiteralPath $driverSource -Destination (Join-Path $stage 'driver\Games8thGuard.sys')

@'
@echo off
cd /d "%~dp0"
start "" "%~dp0Games8Th.Team-Feilian-CLI.exe"
'@ | Set-Content -LiteralPath (Join-Path $stage 'start-Games8thBlocker.bat') -Encoding ASCII

@'
Games8Th.Team 飞连专用 CLI 便携版（强制驱动模式）

1. 运行 start-Games8thBlocker.bat 或直接运行 Games8Th.Team-Feilian-CLI.exe。
2. 启动先显示 Games8Th.Team 标识 2 秒，再自动检测飞连路径、进程和服务。
3. 程序只对实际发现的飞连目标实施屏蔽，并使用 Games8thGuard.sys；驱动未通过服务、设备和
   QUERY_PATHS 三项校验时，结果为 UNVERIFIED，不会回退到 WFP/防火墙。
4. driver\Games8thGuard.sys 必须有当前 Windows 策略信任的有效签名。
   便携包不包含签名绕过，也不会修改 Secure Boot、DSE 或测试签名设置。
5. 需要清理时，运行 Games8Th.Team-Feilian-CLI.exe cli clear。
6. 跳过飞连检测直接测试指定软件：
   Games8Th.Team-Feilian-CLI.exe cli test "C:\Path\Target.exe"
   目标必须是当前存在的 EXE；只有驱动下发和 QUERY_PATHS 完整路径回读均成功才报告 OK。

命令行：
  Games8Th.Team-Feilian-CLI.exe
  Games8Th.Team-Feilian-CLI.exe cli auto
  Games8Th.Team-Feilian-CLI.exe cli driver-status
  Games8Th.Team-Feilian-CLI.exe cli list
  Games8Th.Team-Feilian-CLI.exe cli clear
  Games8Th.Team-Feilian-CLI.exe cli test "C:\Path\Target.exe"
'@ | Set-Content -LiteralPath (Join-Path $stage 'portable-readme.txt') -Encoding UTF8

$hashLines = Get-ChildItem -LiteralPath $stage -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($stage.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
$hashLines | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ASCII

Write-Output "Portable release written to: $stage"
Get-ChildItem -LiteralPath $stage -Recurse -File |
    Sort-Object FullName |
    Select-Object FullName, Length
