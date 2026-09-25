param([int]$Round = 1)

$ErrorActionPreference = "Stop"
$root = "C:\Users\Administrator\Desktop\GPT\内部\飞连屏蔽插件"
$wfp  = Join-Path $root "src\WfpEngine.cs"
$prog = Join-Path $root "src\Program.cs"
$bat  = Join-Path $root "build.bat"

$code = [IO.File]::ReadAllText($wfp)
$progText = [IO.File]::ReadAllText($prog)
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

# ---- 5. 主程序集成 ----
Check "INTEG/apply_fallback" ($progText -match 'WfpEngine\.IsAvailable\(\)') "ApplyJob 未接入 WFP 回退"
Check "INTEG/block_dir"      ($progText -match 'WfpEngine\.BlockDirectory') "未调用 BlockDirectory"
Check "INTEG/clear"          ($progText -match 'WfpEngine\.ClearAll') "ClearAll 未调用 WFP 清理"
Check "INTEG/self_exclude"   ($code -match 'SelfPath') "缺少自身路径排除"

# ---- 6. build.bat 包含新文件 ----
Check "BUILD/wfp_included" ($batText -match 'src\\WfpEngine\.cs') "build.bat 未包含 WfpEngine.cs"

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
