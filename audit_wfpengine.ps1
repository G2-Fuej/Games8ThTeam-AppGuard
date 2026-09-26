param([int]$Round = 1)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$wfp  = Join-Path $root "src\WfpEngine.cs"
$prog = Join-Path $root "src\FeilianCli.cs"
$bat  = Join-Path $root "build.bat"

$code = [IO.File]::ReadAllText($wfp)
$progText = [IO.File]::ReadAllText($prog)
$embedded = [IO.File]::ReadAllText((Join-Path $root "src\EmbeddedDriverInstaller.cs"))
$batText = [IO.File]::ReadAllText($bat)

$fails = New-Object System.Collections.Generic.List[string]
$pass  = New-Object System.Collections.Generic.List[string]
function Check($name, $ok, $detail) {
    if ($ok) { $script:pass.Add($name) }
    else     { $script:fails.Add("$name :: $detail") }
}

# ---- 1. 真实 SDK GUID ----
Check "GUID/layer_v4"  ($code -match 'c38d57d1-05a7-4c33-904f-7fbceee60e82') "ALE_AUTH_CONNECT_V4 GUID 错误"
Check "GUID/layer_v6"  ($code -match '4a72393b-319f-44bc-84c3-ba54dcb3b6b4') "ALE_AUTH_CONNECT_V6 GUID 错误"
Check "GUID/condition" ($code -match 'd78e1e87-8644-4ea5-9437-d809ecefc971') "FWPM_CONDITION_ALE_APP_ID GUID 错误"
Check "GUID/sublayer"  ($code -match 'eebecc03-ced4-4380-819a-2734397b2b74') "FWPM_SUBLAYER_UNIVERSAL GUID 错误"

# ---- 2. 枚举值（Windows SDK 真实值）----
Check "ENUM/uint64"    ($code -match 'FWP_UINT64 = 4') "FWP_UINT64 应为 4"
Check "ENUM/blob"      ($code -match 'FWP_BYTE_BLOB_TYPE = 12') "FWP_BYTE_BLOB_TYPE 应为 12"
Check "ENUM/action"    ($code -match 'FWP_ACTION_BLOCK = 0x1001') "FWP_ACTION_BLOCK 应为 0x1001 (含 TERMINATING)"
Check "ENUM/match"     ($code -match 'FWP_MATCH_EQUAL = 0') "FWP_MATCH_EQUAL 应为 0"

# ---- 3. 结构大小（用反射需要编译，此处做布局关键字断言）----
Check "STRUCT/providerdata_value" ($code -match 'public FWP_BYTE_BLOB ProviderData;') "providerData 应为值类型 FWP_BYTE_BLOB"
Check "STRUCT/effective_weight"   ($code -match 'public FWP_VALUE0 EffectiveWeight;') "缺少 effectiveWeight 字段"
Check "STRUCT/action_pad"         ($code -match 'private uint _pad;') "FWPM_ACTION0 缺少 4 字节填充"
Check "STRUCT/filter_union_16"    ($code -match 'Size = 16') "FWPM_FILTER0 union 应为 16 字节"
Check "STRUCT/provider_servername"($code -match 'public string ServiceName;') "FWPM_PROVIDER0 缺少 serviceName"

# ---- 4. SeDebug P/Invoke 编组 ----
Check "SEDEBUG/unicode" (([regex]::Matches($code, "CharSet = CharSet.Unicode")).Count -ge 3) "LookupPrivilegeValueW 应为 Unicode 编组（至少 3 处 CharSet.Unicode）"
Check "SEDEBUG/called"  ($code -match 'EnableSeDebugPrivilege\(\);') "未调用 EnableSeDebugPrivilege"

# ---- 5. 主程序集成：驱动唯一运行时后端 ----
# WfpEngine.cs remains a static WFP layout/reference audit target.  It must
# not be reachable as a runtime fallback from the user-mode application.
Check "RUNTIME/no_wfp_calls" (
    ([regex]::Matches($progText, '\bWfpEngine\s*\.\s*[A-Za-z_]')).Count -eq 0
) "FeilianCli.cs 仍调用 WfpEngine 作为运行时后端"
Check "RUNTIME/no_firewall_calls" (
    ([regex]::Matches($progText, '\bFirewall\s*\.\s*[A-Za-z_]')).Count -eq 0
) "FeilianCli.cs 仍调用 Windows Firewall 作为运行时后端"
Check "RUNTIME/apply_driver" (
    ($progText -match 'KernelDriver\.GetStatus\(\)') -and
    ($progText -match 'KernelDriver\.AddBlockedPath') -and
    ($progText -match 'KernelDriver\.ContainsBlockedPath')
) "Apply 路径缺少驱动状态、下发或回读校验"
$kernelText = [IO.File]::ReadAllText((Join-Path $root 'src\KernelDriver.cs'))
Check "RUNTIME/wfp_appid" (
    ($kernelText -match 'FwpmGetAppIdFromFileName0') -and
    ($kernelText -match 'FwpByteBlob') -and
    ($kernelText -match 'FwpmFreeMemory0')
) "驱动下发仍使用 DOS 路径，未转换为 ALE_APP_ID"
Check "RUNTIME/clear_driver" (
    ($progText -match 'KernelDriver\.ClearAll\(\)') -and
    ($progText -match 'KernelDriver\.TryQueryBlockedPaths')
) "Clear 路径缺少驱动清理或回读校验"
Check "INTEG/self_exclude"   ($code -match 'SelfPath') "缺少自身路径排除"

# ---- 6. CLI-only product build ----
Check "CLI/splash_2s" (
    ($progText -match 'ShowLogo\(\)') -and ($progText -match 'Thread\.Sleep\(2000\)')
) "CLI 缺少 Games8Th.Team 标识或 2 秒展示"
Check "CLI/auto_discovery" (
    ($progText -match 'DiscoverFeilianTargets\(\)') -and
    ($progText -match 'ScanProcesses\(found\)') -and
    ($progText -match 'ScanServices\(found\)') -and
    ($progText -match 'ScanUninstallRegistry\(found\)')
) "CLI 自动飞连发现不完整"
Check "CLI/corplink_root" (
    ($progText -match 'FeilianInstallRoot\s*=\s*@"C:\\Program Files\\CorpLink"') -and
    ($progText -match 'AddExecutablesUnder\(FeilianInstallRoot, found\)')
) "CLI 未将 C:\Program Files\CorpLink 设为飞连专属发现根目录"
Check "CLI/corplink_boundary" (
    ($progText -match 'IsUnderFeilianInstallRoot\(string path\)') -and
    ($progText -match 'candidate\.StartsWith\(root \+ "\\\\"')
) "CorpLink 路径判断缺少目录边界，可能误收 CorpLink2"
Check "CLI/exe_only_targets" (
    ($progText -match 'Directory\.GetFiles\(current, "\*\.exe"') -and
    ($progText -match 'Path\.GetExtension\(candidate\), "\.exe"') -and
    (-not ($progText -match 'found\.Add\(.*Directory'))
) "飞连发现结果未严格限制为真实 EXE"
Check "CLI/corplink_sources_constrained" (
    ([regex]::Matches($progText, 'AddExecutableTarget\(').Count -ge 4) -and
    ($progText -match 'IsUnderFeilianInstallRoot\(installLocation\)')
) "进程、服务或注册表发现未统一经过 CorpLink 根目录约束"
Check "CLI/reparse_guard" (
    ([regex]::Matches($progText, 'FileAttributes\.ReparsePoint').Count -ge 2)
) "CorpLink 递归扫描缺少目录或 EXE 重解析点防护"
Check "CLI/driver_autoload" (
    ($progText -match 'EmbeddedDriverInstaller\.EnsureLoaded\(\)') -and
    ($embedded -match 'GetManifestResourceStream') -and
    ($embedded -match 'CreateService\(') -and
    ($embedded -match 'WinVerifyTrust')
) "CLI 未从内嵌资源校验并加载签名驱动"
Check "CLI/single_exe_resource" (
    ($batText -match '/resource:.*Games8thGuard\.sys') -and
    ($batText -match 'EmbeddedDriverInstaller\.cs') -and
    (-not ($progText -match 'load\.bat'))
) "构建未把驱动集成到单 EXE，或仍依赖外部 load.bat"
Check "CLI/self_exclude" (
    ($progText -match 'IsSelfPath\(candidate\)') -and
    ($progText -match 'Process\.GetCurrentProcess\(\)\.MainModule\.FileName')
) "CLI 自动发现未排除工具自身"
Check "CLI/direct_test" (
    ($progText -match 'RunDirectTest\(args\)') -and
    ($progText -match 'File\.Exists\(target\)') -and
    ($progText -match 'Path\.GetExtension\(target\)') -and
    ($progText -match 'KernelDriver\.AddBlockedPath\(target\)') -and
    ($progText -match 'KernelDriver\.ContainsBlockedPath\(target\)')
) "CLI 测试模式未执行真实 EXE 校验、驱动下发或 QUERY_PATHS 回读"
Check "BUILD/cli_only" (
    ($batText -match '/target:exe') -and
    ($batText -match '/platform:x64') -and
    ($batText -match '/warnaserror\+') -and
    ($batText -match 'src\\FeilianCli\.cs') -and
    ($batText -match 'src\\KernelDriver\.cs') -and
    (-not ($batText -match 'src\\Program\.cs')) -and
    (-not ($batText -match 'src\\WfpEngine\.cs')) -and
    (-not ($batText -match 'System\.Windows\.Forms'))
) "build.bat 未形成 CLI-only 构建"

# ---- 7. 语法级机械平衡 ----
$ob = ([regex]::Matches($code, '\{')).Count; $cb = ([regex]::Matches($code, '\}')).Count
Check "STRUCT/braces" ($ob -eq $cb) "braces $ob/$cb"

Write-Output "===== WFP ENGINE AUDIT ROUND $Round ====="
Write-Output "PASS: $($pass.Count)"
foreach ($p in $pass) { Write-Output "  [OK]   $p" }
Write-Output "FAIL: $($fails.Count)"
foreach ($f in $fails) { Write-Output "  [FAIL] $f" }
if ($fails.Count -eq 0) { Write-Output "ROUND $Round RESULT: CLEAN"; exit 0 }
else { Write-Output "ROUND $Round RESULT: ISSUES FOUND -> fix then restart"; exit 1 }
