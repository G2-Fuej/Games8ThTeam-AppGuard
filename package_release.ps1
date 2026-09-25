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

if (-not (Test-Path -LiteralPath (Join-Path $root 'Games8thBlocker.exe'))) {
    throw 'Games8thBlocker.exe was not produced'
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'assets') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'driver') -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $root 'Games8thBlocker.exe') -Destination $stage
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

$builtDriver = Join-Path $root 'driver\build\Release\Games8thGuard.sys'
if (Test-Path -LiteralPath $builtDriver) {
    Copy-Item -LiteralPath $builtDriver -Destination (Join-Path $stage 'driver\Games8thGuard.sys')
}

@'
@echo off
cd /d "%~dp0"
start "" "%~dp0Games8thBlocker.exe"
'@ | Set-Content -LiteralPath (Join-Path $stage 'start-Games8thBlocker.bat') -Encoding ASCII

@'
Games8Th.Team 便携版

1. 双击 start-Games8thBlocker.bat 或直接运行 Games8thBlocker.exe。
2. 程序会请求管理员权限；首次启动会自动搜索飞连进程、服务和常见安装目录。
3. WFP/Windows 防火墙路径不要求安装开发工具或 WDK。
4. 需要清理时，在程序中点击“解除全部限制”。
5. driver\Games8thGuard.sys 仅是可选内核路径；未加载时程序自动使用用户态 WFP。

命令行：
  Games8thBlocker.exe cli feilian
  Games8thBlocker.exe cli block "C:\Path\To\App"
  Games8thBlocker.exe cli list
  Games8thBlocker.exe cli clear
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
