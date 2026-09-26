param(
    [int]$Round = 1
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "driver\g8tguard.c"
$code = [IO.File]::ReadAllText($src)

$fails = New-Object System.Collections.Generic.List[string]
$pass = New-Object System.Collections.Generic.List[string]

function Check($name, $ok, $detail) {
    if ($ok) { $script:pass.Add($name) }
    else { $script:fails.Add("$name :: $detail") }
}

function FunctionBody($name) {
    $m = [regex]::Match(
        $code,
        "(?ms)\b$name\s*\([^)]*\)\s*\{(?<body>.*?)\n\}",
        [Text.RegularExpressions.RegexOptions]::Multiline
    )
    if ($m.Success) { return $m.Groups["body"].Value }
    return ""
}

# 1. Basic source integrity.
$ob = ([regex]::Matches($code, "\{")).Count
$cb = ([regex]::Matches($code, "\}")).Count
$op = ([regex]::Matches($code, "\(")).Count
$cp = ([regex]::Matches($code, "\)")).Count
Check "STRUCT/braces" ($ob -eq $cb) "open=$ob close=$cb"
Check "STRUCT/parens" ($op -eq $cp) "open=$op close=$cp"

# 2. WFP classify callbacks must tolerate an absent fixed-value pointer.
$v4 = FunctionBody "g8tClassifyAuthConnectV4"
$v6 = FunctionBody "g8tClassifyAuthConnectV6"
Check "CLASSIFY/v4_found" ($v4.Length -gt 0) "V4 classify not found"
Check "CLASSIFY/v6_found" ($v6.Length -gt 0) "V6 classify not found"
Check "CLASSIFY/v4_null_guard" ($v4 -match "inFixedValues\s*==\s*NULL") "V4 dereferences inFixedValues without a guard"
Check "CLASSIFY/v6_null_guard" ($v6 -match "inFixedValues\s*==\s*NULL") "V6 dereferences inFixedValues without a guard"

# 3. User input must be a complete UTF-16 string and must be bounded.
$block = [regex]::Match($code, "(?ms)case\s+IOCTL_G8T_BLOCK_PATH:.*?break;").Value
$unblock = [regex]::Match($code, "(?ms)case\s+IOCTL_G8T_UNBLOCK_PATH:.*?break;").Value
Check "IOCTL/block_even_length" ($block -match "inLen\s*%\s*sizeof\(WCHAR\)") "BLOCK accepts odd byte lengths"
Check "IOCTL/unblock_even_length" ($unblock -match "inLen\s*%\s*sizeof\(WCHAR\)") "UNBLOCK accepts odd byte lengths"
Check "IOCTL/block_input_guard" ($block -match "inLen\s*>=\s*sizeof\(WCHAR\)") "BLOCK input length guard missing"
Check "IOCTL/unblock_input_guard" ($unblock -match "inLen\s*>=\s*sizeof\(WCHAR\)") "UNBLOCK input length guard missing"
Check "IOCTL/query_output_guard" ($code -match "outLen\s*>=\s*needed") "QUERY output length guard missing"

# 4. Kernel-safe memory operations.
Check "MEM/no_crt_memmove" (-not ($code -match "\bmemmove\s*\(")) "CRT memmove remains in kernel path"
Check "MEM/rtl_move" ($code -match "RtlMoveMemory\s*\(") "RtlMoveMemory is not used for in-kernel compaction"
Check "MEM/nonpaged_query_temp" ($code -match "ExAllocatePoolWithTag\(NonPagedPoolNx") "QUERY does not use NonPagedPoolNx"
Check "MEM/query_temp_free" ($code -match "ExFreePoolWithTag") "QUERY temporary pool is not freed"

# 5. Spin-lock and pool lifetime checks.
$lockAcquires = ([regex]::Matches($code, "KeAcquireSpinLock\s*\(")).Count
$lockReleases = ([regex]::Matches($code, "KeReleaseSpinLock\s*\(")).Count
Check "LOCK/global_pairing" ($lockReleases -ge $lockAcquires) "acquires=$lockAcquires releases=$lockReleases"
foreach ($lockFunction in @("g8tIsPathBlocked", "g8tAddPath", "g8tRemovePath", "g8tClearAll")) {
    $lockBody = FunctionBody $lockFunction
    Check "LOCK/$lockFunction" (
        (($lockBody -match "KeAcquireSpinLock\s*\(") -and
         ($lockBody -match "KeReleaseSpinLock\s*\("))
    ) "lock acquire/release pair missing"
}
$query = [regex]::Match($code, "(?ms)case\s+IOCTL_G8T_QUERY_PATHS:.*?\n\s*default:").Value
$lockedQuery = [regex]::Match($query, "(?ms)KeAcquireSpinLock\(&g_lock.*?KeReleaseSpinLock\(&g_lock").Value
Check "LOCK/query_copy_outside_lock" (-not ($lockedQuery -match "RtlCopyMemory\s*\(\s*sysBuf")) "SystemBuffer copy occurs while spin lock is held"
Check "LOCK/no_pool_free_under_lock" (-not ($lockedQuery -match "ExFreePool")) "pool free occurs while spin lock is held"

# 6. WFP lifetime and failure cleanup.
$unload = FunctionBody "g8tUnload"
$closeAt = $unload.IndexOf("FwpmEngineClose0")
$unregisterAt = $unload.IndexOf("g8tUnregisterCallouts")
Check "WFP/unload_order" (($closeAt -ge 0) -and ($unregisterAt -ge 0) -and ($closeAt -lt $unregisterAt)) "engine close must precede callout unregister"
Check "WFP/handle_null_after_close" ($unload -match "g_engineHandle\s*=\s*NULL") "engine handle is not cleared after close"
Check "WFP/failure_cleanup" ($code -match "cleanup_engine:[\s\S]*FwpmEngineClose0[\s\S]*g8tUnregisterCallouts") "registration failure cleanup is incomplete"
Check "WFP/notify_callback" ($code -match "callout\.notifyFn\s*=\s*g8tCalloutNotify") "callout notifyFn is NULL or not assigned"
Check "WFP/transaction_abort" ($code -match "transactionStarted[\s\S]*FwpmTransactionAbort0") "failed WFP transaction is not aborted"

# 7. IRP completion and device cleanup.
$deviceControl = FunctionBody "g8tDeviceControl"
Check "IRP/device_control_complete" ($deviceControl -match "IoCompleteRequest\s*\(") "DEVICE_CONTROL IRP is not completed"
Check "IRP/create_complete" ((FunctionBody "g8tCreate") -match "IoCompleteRequest\s*\(") "CREATE IRP is not completed"
Check "IRP/close_complete" ((FunctionBody "g8tClose") -match "IoCompleteRequest\s*\(") "CLOSE IRP is not completed"
Check "DEVICE/symbolic_link_cleanup" ($code -match "IoDeleteSymbolicLink\(&g_symbolicName\)") "symbolic link cleanup missing"

# 8. No pageable classify path markers.
Check "IRQL/no_pageable_classify" (-not ($code -match "PAGED_CODE\s*\(\)")) "classify path contains PAGED_CODE"

Write-Output "===== W11 DRIVER BSOD AUDIT ROUND $Round ====="
Write-Output "PASS: $($pass.Count)"
foreach ($p in $pass) { Write-Output "  [OK]   $p" }
Write-Output "FAIL: $($fails.Count)"
foreach ($f in $fails) { Write-Output "  [FAIL] $f" }
if ($fails.Count -eq 0) {
    Write-Output "ROUND $Round RESULT: CLEAN"
    exit 0
}
Write-Output "ROUND $Round RESULT: ISSUES FOUND -> fix then restart 3-round cycle"
exit 1
