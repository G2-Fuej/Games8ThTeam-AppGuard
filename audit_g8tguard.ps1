param(
    [int]$Round = 1
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root "driver\g8tguard.c"
$bat  = Join-Path $root "driver\build.bat"
$vcx  = Join-Path $root "driver\g8tguard.vcxproj"

$code = [IO.File]::ReadAllText($src)
$batText = [IO.File]::ReadAllText($bat)
$vcxText = [IO.File]::ReadAllText($vcx)

$fails = New-Object System.Collections.Generic.List[string]
$pass  = New-Object System.Collections.Generic.List[string]

function Check($name, $ok, $detail) {
    if ($ok) { $script:pass.Add($name) }
    else     { $script:fails.Add("$name :: $detail") }
}

# ---- 1. structural integrity ----
$ob = ([regex]::Matches($code, '\{')).Count
$cb = ([regex]::Matches($code, '\}')).Count
$op = ([regex]::Matches($code, '\(')).Count
$cp = ([regex]::Matches($code, '\)')).Count
Check "STRUCT/braces" ($ob -eq $cb) "open=$ob close=$cb"
Check "STRUCT/parens" ($op -eq $cp) "open=$op close=$cp"

# ---- 2. WFP engine session lifetime ----
Check "LIFE/global_handle" ($code -match 'static\s+HANDLE\s+g_engineHandle') "缺少全局 engine 句柄"
$onSuccess = $code -match 'g_engineHandle\s*=\s*engineHandle'
Check "LIFE/save_on_success" $onSuccess "成功路径未保存 engine 句柄"
Check "LIFE/no_unregister_on_success" (
    $code -match 'g_engineHandle\s*=\s*engineHandle;[\s\S]{0,200}return\s+STATUS_SUCCESS;'
) "成功路径疑似仍会注销 callout"
Check "LIFE/notify_callback" (
    ($code -match 'callout\.notifyFn\s*=\s*g8tCalloutNotify') -and
    ($code -match 'g8tCalloutNotify\s*\(')
) "FWPS_CALLOUT1 notifyFn 为空或通知回调缺失"
Check "LIFE/custom_sublayer" (
    ($code -match 'DEFINE_GUID\(G8T_SUBLAYER') -and
    ($code -match 'FwpmSubLayerAdd0') -and
    (([regex]::Matches($code, 'filter\.subLayerKey\s*=\s*G8T_SUBLAYER')).Count -ge 2)
) "未创建/使用专用 WFP sublayer"
Check "LIFE/transaction" (
    ($code -match 'FwpmTransactionBegin0') -and
    ($code -match 'FwpmTransactionCommit0') -and
    ($code -match 'FwpmTransactionAbort0')
) "WFP 管理对象未使用事务或失败路径未回滚"

# ---- 3. unload ordering: engine close BEFORE callout unregister ----
$unload = [regex]::Match($code, 'static\s+VOID\s*\r?\n\s*g8tUnload\([^)]*\)\s*\{[\s\S]*?\r?\n\}').Value
$iClose = $unload.IndexOf("FwpmEngineClose0")
$iUnreg = $unload.IndexOf("g8tUnregisterCallouts")
Check "UNLOAD/order" (($iClose -ge 0) -and ($iUnreg -ge 0) -and ($iClose -lt $iUnreg)) `
    "engine close idx=$iClose, unregister idx=$iUnreg（必须 close 在前）"

# ---- 4. classify: every action write guarded by rights check ----
$v4 = [regex]::Match($code, 'g8tClassifyAuthConnectV4\([\s\S]*?\r?\n\}\r?\n').Value
$v6 = [regex]::Match($code, 'g8tClassifyAuthConnectV6\([\s\S]*?\r?\n\}\r?\n').Value
Check "CLASSIFY/v4_present" ($v4.Length -gt 0) "未定位 V4 classify"
Check "CLASSIFY/v6_present" ($v6.Length -gt 0) "未定位 V6 classify"
foreach ($pair in @(@('V4',$v4), @('V6',$v6))) {
    $tag = $pair[0]; $body = $pair[1]
    $guardCount = ([regex]::Matches($body, 'rights\s*&\s*FWPS_RIGHT_ACTION_WRITE')).Count
    $blockCount = ([regex]::Matches($body, 'FWP_ACTION_BLOCK')).Count
    $permitCount = ([regex]::Matches($body, 'FWP_ACTION_PERMIT')).Count
    Check "CLASSIFY/$tag-guards" ($guardCount -ge ($blockCount + $permitCount)) `
        "guards=$guardCount actions=$($blockCount+$permitCount)"
}

# ---- 5. path resolution alignment + zeroing on BOTH branches ----
Check "RESOLVE/v4_align"  ($code -match 'appIdLen\s*&=\s*~\(sizeof\(WCHAR\)\s*-\s*1\)') "V4 缺偶数对齐"
Check "RESOLVE/v4_zero"   ($code -match 'RtlZeroMemory\(processName') "V4 缺清零"
Check "RESOLVE/v6_align"  ($code -match '\blen\s*&=\s*~\(sizeof\(WCHAR\)\s*-\s*1\)') "V6 缺偶数对齐"
Check "RESOLVE/v6_zero"   ($code -match 'RtlZeroMemory\(processPath') "V6 缺清零"

# ---- 6. no memcpy to SystemBuffer while holding spinlock ----
$query = [regex]::Match($code, 'case\s+IOCTL_G8T_QUERY_PATHS:[\s\S]*?\r?\n\s*\}\r?\n\s*default:').Value
$lockBody = [regex]::Match($query, 'KeAcquireSpinLock\(&g_lock, &irql\);([\s\S]*?)KeReleaseSpinLock\(&g_lock, &irql\);').Groups[1].Value
Check "QUERY/no_locked_sysbuf_copy" (-not ($lockBody -match 'RtlCopyMemory\s*\(\s*sysBuf')) `
    "QUERY_PATHS 在持锁区间内直接拷贝到 SystemBuffer"
Check "QUERY/uses_nonpaged_temp" ($query -match 'ExAllocatePoolWithTag\(NonPagedPoolNx') "QUERY_PATHS 未用 NonPagedPoolNx 临时缓冲"
Check "QUERY/frees_temp" ($query -match 'ExFreePoolWithTag') "QUERY_PATHS 未释放临时池"

# ---- 7. pool discipline ----
Check "POOL/no_NonPagedPool" (-not ($code -match 'NonPagedPool(?!Nx)')) "使用了已弃用的 NonPagedPool"
Check "POOL/tag_defined" ($code -match "#define\s+G8T_POOL_TAG") "缺少池标签"

# ---- 8. wildcard path boundary ----
Check "MATCH/boundary" ($code -match 'return\s+haystack\[needleLen\]\s*==\s*L''\\\\'';') "非通配分支缺路径边界校验"

# ---- 9. IOCTL input bounds ----
Check "IOCTL/inlen_guard" (([regex]::Matches($code, 'inLen\s*>=\s*sizeof\(WCHAR\)')).Count -ge 2) `
    "BLOCK/UNBLOCK 输入长度守卫不足"
Check "IOCTL/outlen_guard" ($code -match 'outLen\s*>=\s*needed') "QUERY 输出长度守卫缺失"

# ---- 10. build hardening ----
Check "BUILD/gs_enabled" (($batText -match '/GS(?![-\w])') -and (-not ($batText -match '/GS-'))) `
    "build.bat 未启用 /GS 栈保护"
Check "BUILD/cfg_nx_aslr" (
    ($batText -match '/guard:cf') -and ($batText -match '/dynamicbase') -and
    ($batText -match '/nxcompat') -and ($batText -match 'bufferoverflowfastfailk\.lib')
) "build.bat 缺少 CFG、ASLR、NX 或内核栈保护支持库"
Check "BUILD/spectre" (($vcxText -match '<SpectreMitigation>true</SpectreMitigation>') -and `
                      ($vcxText -match '<Driver_SpectreMitigation>true</Driver_SpectreMitigation>')) `
    "vcxproj Spectre 缓解未开启"
Check "BUILD/working_dir" ($batText -match 'cd /d\s+"%~dp0\.\."') "driver\build.bat 未固定工作目录"
Check "BUILD/no_signed_overwrite" ($batText -match 'Games8thGuard-unsigned\.sys') "驱动构建会覆盖已签名发布文件"

# ---- 11. no dead code ----
Check "DEAD/no_blockedparentpids" (-not ($code -match 'BlockedParentPids')) "残留 BlockedParentPids"
Check "DEAD/no_calloutsRegistered" (-not ($code -match 'g_calloutsRegistered')) "残留 g_calloutsRegistered"

# ---- report ----
Write-Output "===== W11 DRIVER AUDIT ROUND $Round ====="
Write-Output "PASS: $($pass.Count)"
foreach ($p in $pass) { Write-Output "  [OK]   $p" }
Write-Output "FAIL: $($fails.Count)"
foreach ($f in $fails) { Write-Output "  [FAIL] $f" }
if ($fails.Count -eq 0) {
    Write-Output "ROUND $Round RESULT: CLEAN"
    exit 0
} else {
    Write-Output "ROUND $Round RESULT: ISSUES FOUND -> fix then restart 3-round cycle"
    exit 1
}
