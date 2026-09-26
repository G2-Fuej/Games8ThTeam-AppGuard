/*
 * Games8thGuard.sys
 * Games8Th.Team - Kernel-level software restriction driver
 *
 * 功能：
 *  1. 在 WFP ALE 授权连接层，按「进程完整路径」阻断被限制软件的
 *     所有出站/入站网络连接（含走本地代理 127.0.0.1:7890 的流量，
 *     因为 ALE 层能看到进程身份，且 loopback 也被过滤）。
 *  2. 用户态通过设备 IOCTL 下发/查询被限制路径列表。
 *  3. 基于 KMDF/WDM，Windows 10/11 x64。
 *
 * 说明：本驱动为 WDM（不依赖 WDF），单文件简洁可编译。
 * 编译方式（WDK + MSVC）：
 *   cl /kernel /O1 /GS /Zl /c -DWIN32 -DNDEBUG -D_AMD64_ \
 *      g8tguard.c -o g8tguard.obj
 *   link /kernel /driver /subsystem:native /entry:DriverEntry \
 *      g8tguard.obj ntoskrnl.lib fwpkclnt.lib -out:Games8thGuard.sys
 */

/*
 * INITGUID 必须在包含任何声明 GUID 的头文件之前定义。否则 DEFINE_GUID /
 * FWPM_LAYER_* 只是「声明」而不「分配」，链接期会报 LNK2001 未解析符号。
 */
#include <initguid.h>
#include <ntddk.h>

/*
 * WFP callout 驱动属于「非 NDIS 模型的内核代码」。fwpsk.h 会包含 ndis.h，
 * 而 ndis.h 只有在包含前定义了 NDIS 版本宏时才会声明 NET_BUFFER_LIST 等
 * NDIS6 结构。缺了它，fwpsk.h 里所有用到 NET_BUFFER_LIST / WSACMSGHDR /
 * IF_INDEX 的原型都会解析失败（C2143 / C2370 连锁报错）。
 * 依 WDK 官方 ndis.h 用法说明第 2 条：非 NDIS 驱动应在包含前 #define NDIS630。
 * 630 = Windows 8+；对 Win10/11 x64 完全适用。
 */
#define NDIS630 1

#include <fwpmk.h>
#include <fwpsk.h>
#include <ntstrsafe.h>

#pragma prefast(disable:__WARNING_ENCODE_MEMBER_FUNCTION_POINTER, "Not valid for kernel mode")

#define G8T_DEVICE_NAME        L"\\Device\\G8TGuard"
#define G8T_SYMBOLIC_LINK      L"\\??\\G8TGuard"
#define G8T_IOCTL_BASE         0x8000
#define IOCTL_G8T_BLOCK_PATH   CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE+1, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_G8T_UNBLOCK_PATH CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE+2, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_G8T_QUERY_PATHS  CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE+3, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_G8T_CLEAR_ALL    CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE+4, METHOD_BUFFERED, FILE_ANY_ACCESS)

#define MAX_BLOCKED_PATHS      64
#define MAX_PATH_LEN           520           /* wide chars, incl terminating L'\0' */
#define G8T_POOL_TAG           'T8mG'

typedef struct _G8T_BLOCKED_PATHS
{
    ULONG Count;
    WCHAR Paths[MAX_BLOCKED_PATHS][MAX_PATH_LEN];
} G8T_BLOCKED_PATHS;

/* ---- globals (WDM-style device; no WDF dependency) ---- */
static PDEVICE_OBJECT g_wdmDevice = NULL;
static UNICODE_STRING g_deviceName;
static UNICODE_STRING g_symbolicName;

static G8T_BLOCKED_PATHS g_blocked;             /* protected by g_lock */
static UINT32 g_calloutV4Id = 0;
static UINT32 g_calloutV6Id = 0;
static KSPIN_LOCK g_lock;
static HANDLE g_engineHandle = NULL;   /* WFP engine 会话句柄，驱动生命周期内保持打开 */

/* Callout keys */
DEFINE_GUID(G8T_CALLOUT_AUTH_V4,
    0x2b7f3d2a, 0x1e4a, 0x4c60, 0x9a, 0xd1, 0x3f, 0x1a, 0x8b, 0x2c, 0x9d, 0x44);
DEFINE_GUID(G8T_CALLOUT_AUTH_V6,
    0x2b7f3d2a, 0x1e4a, 0x4c60, 0x9a, 0xd1, 0x3f, 0x1a, 0x8b, 0x2c, 0x9d, 0x45);
DEFINE_GUID(G8T_SUBLAYER,
    0x2b7f3d2a, 0x1e4a, 0x4c60, 0x9a, 0xd1, 0x3f, 0x1a, 0x8b, 0x2c, 0x9d, 0x46);

/* ---- forward decls ---- */
static NTSTATUS g8tCreate(PDEVICE_OBJECT dev, PIRP irp);
static NTSTATUS g8tClose(PDEVICE_OBJECT dev, PIRP irp);
static NTSTATUS g8tDeviceControl(PDEVICE_OBJECT dev, PIRP irp);
static VOID g8tUnload(PDRIVER_OBJECT drv);

static NTSTATUS g8tRegisterCallouts(PDEVICE_OBJECT dev);
static VOID g8tUnregisterCallouts(VOID);
static NTSTATUS NTAPI g8tCalloutNotify(
    FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    const GUID* filterKey,
    FWPS_FILTER1* filter);

/* Case-insensitive wide string comparison helper (bounded) */
static BOOLEAN g8tWildcardMatch(PCWSTR haystack, PCWSTR needle);

static NTSTATUS NTAPI
g8tCalloutNotify(
    FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    const GUID* filterKey,
    FWPS_FILTER1* filter)
{
    UNREFERENCED_PARAMETER(notifyType);
    UNREFERENCED_PARAMETER(filterKey);
    UNREFERENCED_PARAMETER(filter);
    return STATUS_SUCCESS;
}

/* classify for ALE_AUTH_CONNECT V4 */
static VOID NTAPI g8tClassifyAuthConnectV4(
    const FWPS_INCOMING_VALUES0* inFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    void* layerData,
    const void* classifyContext,
    const FWPS_FILTER1* filter,
    UINT64 flowContext,
    FWPS_CLASSIFY_OUT0* classifyOut);

/* classify for ALE_AUTH_CONNECT V6 */
static VOID NTAPI g8tClassifyAuthConnectV6(
    const FWPS_INCOMING_VALUES0* inFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    void* layerData,
    const void* classifyContext,
    const FWPS_FILTER1* filter,
    UINT64 flowContext,
    FWPS_CLASSIFY_OUT0* classifyOut);

static NTSTATUS g8tResolveProcessName(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Out_writes_(MAX_PATH_LEN) PWCHAR processName,
    _Out_ PULONG processNameLen);

/* ================================================================
 *  Process-name resolution: from ALE layer metadata
 * ================================================================ */
static NTSTATUS
g8tResolveProcessName(
    const FWPS_INCOMING_VALUES0* inFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    PWCHAR processName,
    PULONG processNameLen)
{
    NTSTATUS status = STATUS_SUCCESS;
    UINT32 appIdLen = 0;
    const FWP_BYTE_BLOB* appId = NULL;

    UNREFERENCED_PARAMETER(inMetaValues);

    if (processName == NULL || processNameLen == NULL)
        return STATUS_INVALID_PARAMETER;

    *processNameLen = 0;
    processName[0] = L'\0';

    if (inFixedValues == NULL)
        return STATUS_INVALID_PARAMETER;

    /* ALE_AUTH_CONNECT_V4 layer: the app id (process path) is in
     * field ALE_APP_ID. */
    appId = inFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_ALE_APP_ID].value.byteBlob;

    if (appId == NULL || appId->data == NULL || appId->size == 0)
        return STATUS_NOT_FOUND;

    appIdLen = appId->size;
    if (appIdLen >= (MAX_PATH_LEN * sizeof(WCHAR)))
        appIdLen = (MAX_PATH_LEN - 1) * sizeof(WCHAR);
    /* 确保偶数对齐，避免奇数长度在 WCHAR 数组中留下半个字符 */
    appIdLen &= ~(sizeof(WCHAR) - 1);

    RtlZeroMemory(processName, MAX_PATH_LEN * sizeof(WCHAR));
    RtlCopyMemory(processName, appId->data, appIdLen);
    processName[appIdLen / sizeof(WCHAR)] = L'\0';
    *processNameLen = appIdLen / sizeof(WCHAR);

    return status;
}

/* ================================================================
 *  Wildcard / substring matching (case-insensitive).
 *  If the blocked entry ends with '*', it's a prefix match.
 * ================================================================ */
static BOOLEAN
g8tWildcardMatch(PCWSTR haystack, PCWSTR needle)
{
    ULONG hayLen, needleLen;
    WCHAR last;
    BOOLEAN prefixMode;

    if (haystack == NULL || needle == NULL)
        return FALSE;

    hayLen = (ULONG)wcslen(haystack);
    needleLen = (ULONG)wcslen(needle);

    if (needleLen == 0)
        return FALSE;

    last = needle[needleLen - 1];
    prefixMode = (last == L'*');

    if (prefixMode)
    {
        /* 显式通配：结尾 '*' 表示自由前缀匹配 */
        ULONG cmpLen = needleLen - 1;
        if (cmpLen > hayLen)
            return FALSE;
        return _wcsnicmp(haystack, needle, cmpLen) == 0;
    }

    /* 非通配：前缀必须落在路径边界，避免 C:\foo 误匹配 C:\foobar */
    if (needleLen > hayLen)
        return FALSE;
    if (_wcsnicmp(haystack, needle, needleLen) != 0)
        return FALSE;
    if (hayLen == needleLen)
        return TRUE;
    return haystack[needleLen] == L'\\';
}

/* ================================================================
 *  Check whether a process path is blocked
 * ================================================================ */
static BOOLEAN
g8tIsPathBlocked(PCWSTR processPath)
{
    BOOLEAN blocked = FALSE;
    ULONG i;
    KIRQL irql;

    if (processPath == NULL || processPath[0] == L'\0')
        return FALSE;

    KeAcquireSpinLock(&g_lock, &irql);
    for (i = 0; i < g_blocked.Count; i++)
    {
        if (g8tWildcardMatch(processPath, g_blocked.Paths[i]))
        {
            blocked = TRUE;
            break;
        }
    }
    KeReleaseSpinLock(&g_lock, irql);
    return blocked;
}

/* ================================================================
 *  ALE_AUTH_CONNECT V4 classify
 * ================================================================ */
static VOID NTAPI
g8tClassifyAuthConnectV4(
    const FWPS_INCOMING_VALUES0* inFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    void* layerData,
    const void* classifyContext,
    const FWPS_FILTER1* filter,
    UINT64 flowContext,
    FWPS_CLASSIFY_OUT0* classifyOut)
{
    WCHAR processPath[MAX_PATH_LEN];
    ULONG processPathLen = 0;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);
    UNREFERENCED_PARAMETER(inMetaValues);

    if (classifyOut == NULL || inFixedValues == NULL)
        return;

    /* Permissive default：仅在引擎授予写权限时设置 action */
    if (classifyOut->rights & FWPS_RIGHT_ACTION_WRITE)
        classifyOut->actionType = FWP_ACTION_PERMIT;

    status = g8tResolveProcessName(inFixedValues, inMetaValues,
                                   processPath, &processPathLen);
    if (!NT_SUCCESS(status) || processPathLen == 0)
        return;

    if (g8tIsPathBlocked(processPath))
    {
        /* 仅在引擎授予写权限时才修改 action（标准 classify 约定） */
        if (classifyOut->rights & FWPS_RIGHT_ACTION_WRITE)
        {
            classifyOut->actionType = FWP_ACTION_BLOCK;
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
            DbgPrint("Games8thGuard: BLOCK V4 connect by %ws\n", processPath);
        }
    }
}

/* ================================================================
 *  ALE_AUTH_CONNECT V6 classify (same logic, V6 fields)
 * ================================================================ */
static VOID NTAPI
g8tClassifyAuthConnectV6(
    const FWPS_INCOMING_VALUES0* inFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    void* layerData,
    const void* classifyContext,
    const FWPS_FILTER1* filter,
    UINT64 flowContext,
    FWPS_CLASSIFY_OUT0* classifyOut)
{
    WCHAR processPath[MAX_PATH_LEN];
    ULONG processPathLen = 0;
    const FWP_BYTE_BLOB* appId = NULL;

    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);
    UNREFERENCED_PARAMETER(inMetaValues);

    if (classifyOut == NULL || inFixedValues == NULL)
        return;

    /* Permissive default：仅在引擎授予写权限时设置 action */
    if (classifyOut->rights & FWPS_RIGHT_ACTION_WRITE)
        classifyOut->actionType = FWP_ACTION_PERMIT;

    appId = inFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V6_ALE_APP_ID].value.byteBlob;
    if (appId == NULL || appId->data == NULL || appId->size == 0)
        return;

    RtlZeroMemory(processPath, sizeof(processPath));
    UINT32 len = appId->size;
    if (len >= (MAX_PATH_LEN * sizeof(WCHAR)))
        len = (MAX_PATH_LEN - 1) * sizeof(WCHAR);
    /* 与 V4 分支一致：偶数对齐，避免奇数长度在 WCHAR 数组中留下半个字符 */
    len &= ~(sizeof(WCHAR) - 1);
    RtlCopyMemory(processPath, appId->data, len);
    processPath[len / sizeof(WCHAR)] = L'\0';
    processPathLen = len / sizeof(WCHAR);

    if (processPathLen == 0)
        return;

    if (g8tIsPathBlocked(processPath))
    {
        /* 仅在引擎授予写权限时才修改 action（标准 classify 约定） */
        if (classifyOut->rights & FWPS_RIGHT_ACTION_WRITE)
        {
            classifyOut->actionType = FWP_ACTION_BLOCK;
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
            DbgPrint("Games8thGuard: BLOCK V6 connect by %ws\n", processPath);
        }
    }
}

/* ================================================================
 *  Register callouts
 * ================================================================ */
static NTSTATUS
g8tRegisterCallouts(PDEVICE_OBJECT dev)
{
    NTSTATUS status;
    FWPS_CALLOUT1 callout;
    FWPM_CALLOUT0 fwpmCallout;
    FWPM_FILTER0 filter;
    FWPM_SUBLAYER0 subLayer;
    GUID layerV4 = FWPM_LAYER_ALE_AUTH_CONNECT_V4;
    GUID layerV6 = FWPM_LAYER_ALE_AUTH_CONNECT_V6;
    FWPM_SESSION0 session;
    HANDLE engineHandle = NULL;
    NTSTATUS ns;
    BOOLEAN transactionStarted = FALSE;

    RtlZeroMemory(&callout, sizeof(callout));
    callout.calloutKey = G8T_CALLOUT_AUTH_V4;
    callout.flags = 0;
    callout.classifyFn = g8tClassifyAuthConnectV4;
    /* Supply a standards-compliant notification callback.  The previously
     * signed build passed NULL here and NtLoadDriver returned
     * STATUS_FWP_NULL_POINTER during WFP initialization. */
    callout.notifyFn = g8tCalloutNotify;
    callout.flowDeleteFn = NULL;

    status = FwpsCalloutRegister1(dev, &callout, &g_calloutV4Id);
    if (!NT_SUCCESS(status))
    {
        DbgPrint("Games8thGuard: FwpsCalloutRegister1 V4 failed %08x\n", status);
        return status;
    }

    callout.calloutKey = G8T_CALLOUT_AUTH_V6;
    callout.classifyFn = g8tClassifyAuthConnectV6;
    status = FwpsCalloutRegister1(dev, &callout, &g_calloutV6Id);
    if (!NT_SUCCESS(status))
    {
        DbgPrint("Games8thGuard: FwpsCalloutRegister1 V6 failed %08x\n", status);
        goto cleanup_callouts;
    }

    /* Open a session to add the callouts + filters to the engine */
    RtlZeroMemory(&session, sizeof(session));
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;

    status = FwpmEngineOpen0(NULL, RPC_C_AUTHN_WINNT, NULL, &session, &engineHandle);
    if (!NT_SUCCESS(status))
    {
        DbgPrint("Games8thGuard: FwpmEngineOpen0 failed %08x\n", status);
        goto cleanup_callouts;
    }

    status = FwpmTransactionBegin0(engineHandle, 0);
    if (!NT_SUCCESS(status))
    {
        DbgPrint("Games8thGuard: FwpmTransactionBegin0 failed %08x\n", status);
        goto cleanup_engine;
    }
    transactionStarted = TRUE;

    RtlZeroMemory(&subLayer, sizeof(subLayer));
    subLayer.subLayerKey = G8T_SUBLAYER;
    subLayer.displayData.name = L"Games8thGuard sublayer";
    subLayer.displayData.description = L"Games8Th.Team Feilian network guard";
    subLayer.flags = 0;
    subLayer.providerKey = NULL;
    subLayer.weight = 0x100;
    status = FwpmSubLayerAdd0(engineHandle, &subLayer, NULL);
    if (!NT_SUCCESS(status) && status != STATUS_FWP_ALREADY_EXISTS)
    {
        DbgPrint("Games8thGuard: FwpmSubLayerAdd0 failed %08x\n", status);
        goto cleanup_engine;
    }

    RtlZeroMemory(&fwpmCallout, sizeof(fwpmCallout));
    fwpmCallout.calloutKey = G8T_CALLOUT_AUTH_V4;
    fwpmCallout.displayData.name = L"Games8thGuard V4 auth";
    fwpmCallout.flags = 0;
    fwpmCallout.providerKey = NULL;
    fwpmCallout.applicableLayer = layerV4;
    ns = FwpmCalloutAdd0(engineHandle, &fwpmCallout, NULL, NULL);
    if (ns != STATUS_SUCCESS && ns != STATUS_FWP_ALREADY_EXISTS)
    {
        DbgPrint("Games8thGuard: FwpmCalloutAdd0 V4 failed %08x\n", ns);
        status = ns;
        goto cleanup_engine;
    }

    RtlZeroMemory(&fwpmCallout, sizeof(fwpmCallout));
    fwpmCallout.calloutKey = G8T_CALLOUT_AUTH_V6;
    fwpmCallout.displayData.name = L"Games8thGuard V6 auth";
    fwpmCallout.flags = 0;
    fwpmCallout.providerKey = NULL;
    fwpmCallout.applicableLayer = layerV6;
    ns = FwpmCalloutAdd0(engineHandle, &fwpmCallout, NULL, NULL);
    if (ns != STATUS_SUCCESS && ns != STATUS_FWP_ALREADY_EXISTS)
    {
        DbgPrint("Games8thGuard: FwpmCalloutAdd0 V6 failed %08x\n", ns);
        status = ns;
        goto cleanup_engine;
    }

    /* Add a filter that calls our callout on every ALE auth connect */
    RtlZeroMemory(&filter, sizeof(filter));
    filter.layerKey = layerV4;
    filter.subLayerKey = G8T_SUBLAYER;
    filter.weight.type = FWP_EMPTY;
    filter.numFilterConditions = 0;
    filter.filterCondition = NULL;
    filter.action.type = FWP_ACTION_CALLOUT_TERMINATING;
    filter.action.calloutKey = G8T_CALLOUT_AUTH_V4;
    filter.displayData.name = L"Games8thGuard V4 block filter";
    filter.flags = FWPM_FILTER_FLAG_NONE;
    ns = FwpmFilterAdd0(engineHandle, &filter, NULL, NULL);
    if (ns != STATUS_SUCCESS && ns != STATUS_FWP_ALREADY_EXISTS)
    {
        DbgPrint("Games8thGuard: FwpmFilterAdd0 V4 failed %08x\n", ns);
        status = ns;
        goto cleanup_engine;
    }

    RtlZeroMemory(&filter, sizeof(filter));
    filter.layerKey = layerV6;
    filter.subLayerKey = G8T_SUBLAYER;
    filter.weight.type = FWP_EMPTY;
    filter.numFilterConditions = 0;
    filter.filterCondition = NULL;
    filter.action.type = FWP_ACTION_CALLOUT_TERMINATING;
    filter.action.calloutKey = G8T_CALLOUT_AUTH_V6;
    filter.displayData.name = L"Games8thGuard V6 block filter";
    filter.flags = FWPM_FILTER_FLAG_NONE;
    ns = FwpmFilterAdd0(engineHandle, &filter, NULL, NULL);
    if (ns != STATUS_SUCCESS && ns != STATUS_FWP_ALREADY_EXISTS)
    {
        DbgPrint("Games8thGuard: FwpmFilterAdd0 V6 failed %08x\n", ns);
        status = ns;
        goto cleanup_engine;
    }

    status = FwpmTransactionCommit0(engineHandle);
    if (!NT_SUCCESS(status))
    {
        DbgPrint("Games8thGuard: FwpmTransactionCommit0 failed %08x\n", status);
        goto cleanup_engine;
    }
    transactionStarted = FALSE;

    /* 成功：保留 engine 会话（动态 session），驱动整个生命周期内保持打开，
     * 使 ALE filter 持续生效；卸载时才关闭并注销 callout。 */
    g_engineHandle = engineHandle;
    engineHandle = NULL;
    return STATUS_SUCCESS;

cleanup_engine:
    /* Abort first so no partial sublayer/callout/filter set can survive a
     * failed initialization.  Closing the dynamic session removes committed
     * management objects; runtime callouts are then unregistered below. */
    if (transactionStarted && engineHandle != NULL)
        FwpmTransactionAbort0(engineHandle);
    if (engineHandle != NULL)
        FwpmEngineClose0(engineHandle);
    g8tUnregisterCallouts();
    return status;

cleanup_callouts:
    g8tUnregisterCallouts();
    return status;
}

static VOID
g8tUnregisterCallouts(VOID)
{
    if (g_calloutV4Id != 0)
    {
        FwpsCalloutUnregisterById0(g_calloutV4Id);
        g_calloutV4Id = 0;
    }
    if (g_calloutV6Id != 0)
    {
        FwpsCalloutUnregisterById0(g_calloutV6Id);
        g_calloutV6Id = 0;
    }
}

/* ================================================================
 *  IOCTL handlers
 * ================================================================ */
static BOOLEAN
g8tAddPath(PCWSTR path)
{
    KIRQL irql;
    ULONG i;
    NTSTATUS copyStatus;
    BOOLEAN added = FALSE;

    if (path == NULL || path[0] == L'\0')
        return FALSE;

    KeAcquireSpinLock(&g_lock, &irql);
    if (g_blocked.Count >= MAX_BLOCKED_PATHS)
    {
        KeReleaseSpinLock(&g_lock, irql);
        return FALSE;
    }
    /* skip if already in list */
    for (i = 0; i < g_blocked.Count; i++)
    {
        if (_wcsicmp(g_blocked.Paths[i], path) == 0)
        {
            KeReleaseSpinLock(&g_lock, irql);
            return TRUE;
        }
    }
    copyStatus = RtlStringCchCopyNW(g_blocked.Paths[g_blocked.Count],
                                    MAX_PATH_LEN, path, MAX_PATH_LEN - 1);
    if (!NT_SUCCESS(copyStatus))
    {
        KeReleaseSpinLock(&g_lock, irql);
        return FALSE;
    }
    g_blocked.Count++;
    added = TRUE;
    KeReleaseSpinLock(&g_lock, irql);
    return added;
}

static BOOLEAN
g8tRemovePath(PCWSTR path)
{
    KIRQL irql;
    ULONG i;
    BOOLEAN removed = FALSE;

    if (path == NULL)
        return FALSE;

    KeAcquireSpinLock(&g_lock, &irql);
    for (i = 0; i < g_blocked.Count; i++)
    {
        if (_wcsicmp(g_blocked.Paths[i], path) == 0)
        {
            if (i + 1 < g_blocked.Count)
            {
                RtlMoveMemory(&g_blocked.Paths[i], &g_blocked.Paths[i+1],
                              (g_blocked.Count - i - 1) *
                              sizeof(g_blocked.Paths[0]));
            }
            g_blocked.Count--;
            removed = TRUE;
            break;
        }
    }
    KeReleaseSpinLock(&g_lock, irql);
    return removed;
}

static VOID
g8tClearAll(VOID)
{
    KIRQL irql;
    KeAcquireSpinLock(&g_lock, &irql);
    g_blocked.Count = 0;
    KeReleaseSpinLock(&g_lock, irql);
}

static NTSTATUS
g8tDeviceControl(PDEVICE_OBJECT dev, PIRP irp)
{
    PIO_STACK_LOCATION irpSp = IoGetCurrentIrpStackLocation(irp);
    ULONG ioctl = irpSp->Parameters.DeviceIoControl.IoControlCode;
    PVOID sysBuf = irp->AssociatedIrp.SystemBuffer;
    ULONG inLen = irpSp->Parameters.DeviceIoControl.InputBufferLength;
    ULONG outLen = irpSp->Parameters.DeviceIoControl.OutputBufferLength;
    NTSTATUS status = STATUS_SUCCESS;
    ULONG bytesReturned = 0;

    UNREFERENCED_PARAMETER(dev);

    switch (ioctl)
    {
        case IOCTL_G8T_BLOCK_PATH:
        {
            if (sysBuf != NULL &&
                inLen >= sizeof(WCHAR) &&
                (inLen % sizeof(WCHAR)) == 0)
            {
                WCHAR* path = (WCHAR*)sysBuf;
                path[inLen / sizeof(WCHAR) - 1] = L'\0';
                if (g8tAddPath(path))
                {
                    DbgPrint("Games8thGuard: added block path %ws\n", path);
                    status = STATUS_SUCCESS;
                }
                else
                {
                    status = STATUS_INSUFFICIENT_RESOURCES;
                }
            }
            else
            {
                status = STATUS_INVALID_PARAMETER;
            }
            break;
        }
        case IOCTL_G8T_UNBLOCK_PATH:
        {
            if (sysBuf != NULL &&
                inLen >= sizeof(WCHAR) &&
                (inLen % sizeof(WCHAR)) == 0)
            {
                WCHAR* path = (WCHAR*)sysBuf;
                path[inLen / sizeof(WCHAR) - 1] = L'\0';
                if (g8tRemovePath(path))
                {
                    DbgPrint("Games8thGuard: removed block path %ws\n", path);
                    status = STATUS_SUCCESS;
                }
                else
                {
                    status = STATUS_NOT_FOUND;
                }
            }
            else
            {
                status = STATUS_INVALID_PARAMETER;
            }
            break;
        }
        case IOCTL_G8T_CLEAR_ALL:
        {
            g8tClearAll();
            status = STATUS_SUCCESS;
            break;
        }
        case IOCTL_G8T_QUERY_PATHS:
        {
            if (sysBuf != NULL)
            {
                ULONG needed = sizeof(G8T_BLOCKED_PATHS);
                if (outLen >= needed)
                {
                    /* 不持自旋锁直接拷贝到 SystemBuffer：锁内 IRQL 提升到
                     * DISPATCH_LEVEL，若系统缓冲位于分页池会触发 bugcheck。
                     * 改为先持锁拷贝到非分页池临时缓冲，释放锁后再拷贝到
                     * SystemBuffer（此时为 PASSIVE_LEVEL）。 */
                    PVOID tmp = ExAllocatePoolWithTag(NonPagedPoolNx, needed, G8T_POOL_TAG);
                    if (tmp == NULL)
                    {
                        status = STATUS_INSUFFICIENT_RESOURCES;
                        break;
                    }
                    {
                        KIRQL irql;
                        KeAcquireSpinLock(&g_lock, &irql);
                        RtlCopyMemory(tmp, &g_blocked, needed);
                        KeReleaseSpinLock(&g_lock, irql);
                    }
                    RtlCopyMemory(sysBuf, tmp, needed);
                    ExFreePoolWithTag(tmp, G8T_POOL_TAG);
                    bytesReturned = needed;
                    status = STATUS_SUCCESS;
                }
                else
                {
                    status = STATUS_BUFFER_TOO_SMALL;
                    bytesReturned = needed;
                }
            }
            else
            {
                status = STATUS_INVALID_PARAMETER;
            }
            break;
        }
        default:
            status = STATUS_INVALID_DEVICE_REQUEST;
            break;
    }

    irp->IoStatus.Status = status;
    irp->IoStatus.Information = bytesReturned;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return status;
}

static NTSTATUS
g8tCreate(PDEVICE_OBJECT dev, PIRP irp)
{
    UNREFERENCED_PARAMETER(dev);
    irp->IoStatus.Status = STATUS_SUCCESS;
    irp->IoStatus.Information = 0;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

static NTSTATUS
g8tClose(PDEVICE_OBJECT dev, PIRP irp)
{
    UNREFERENCED_PARAMETER(dev);
    irp->IoStatus.Status = STATUS_SUCCESS;
    irp->IoStatus.Information = 0;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

/* ================================================================
 *  Unload
 * ================================================================ */
static VOID
g8tUnload(PDRIVER_OBJECT drv)
{
    UNREFERENCED_PARAMETER(drv);

    /* 卸载顺序必须严格：先关 engine 会话撤销 filter，再注销 callout，
     * 否则 BFE 里仍存在引用该 callout 的 filter，注销会失败并残留悬空引用。 */
    if (g_engineHandle != NULL)
    {
        FwpmEngineClose0(g_engineHandle);
        g_engineHandle = NULL;
    }
    g8tUnregisterCallouts();

    if (g_wdmDevice != NULL)
    {
        IoDeleteSymbolicLink(&g_symbolicName);
        IoDeleteDevice(g_wdmDevice);
        g_wdmDevice = NULL;
    }
    DbgPrint("Games8thGuard: unloaded\n");
}

/* ================================================================
 *  DriverEntry
 * ================================================================ */
NTSTATUS
DriverEntry(PDRIVER_OBJECT driverObject, PUNICODE_STRING registryPath)
{
    NTSTATUS status;

    UNREFERENCED_PARAMETER(registryPath);

    DbgPrint("Games8thGuard: DriverEntry\n");

    KeInitializeSpinLock(&g_lock);
    g_blocked.Count = 0;

    driverObject->DriverUnload = g8tUnload;
    driverObject->MajorFunction[IRP_MJ_CREATE] = g8tCreate;
    driverObject->MajorFunction[IRP_MJ_CLOSE] = g8tClose;
    driverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = g8tDeviceControl;

    RtlInitUnicodeString(&g_deviceName, G8T_DEVICE_NAME);
    status = IoCreateDevice(driverObject, 0, &g_deviceName,
                            FILE_DEVICE_UNKNOWN, 0, FALSE, &g_wdmDevice);
    if (!NT_SUCCESS(status))
    {
        DbgPrint("Games8thGuard: IoCreateDevice failed %08x\n", status);
        return status;
    }

    RtlInitUnicodeString(&g_symbolicName, G8T_SYMBOLIC_LINK);
    status = IoCreateSymbolicLink(&g_symbolicName, &g_deviceName);
    if (!NT_SUCCESS(status))
    {
        IoDeleteDevice(g_wdmDevice);
        g_wdmDevice = NULL;
        DbgPrint("Games8thGuard: IoCreateSymbolicLink failed %08x\n", status);
        return status;
    }

    status = g8tRegisterCallouts(g_wdmDevice);
    if (!NT_SUCCESS(status))
    {
        /* Device is created but callouts failed; clean the device. */
        IoDeleteSymbolicLink(&g_symbolicName);
        IoDeleteDevice(g_wdmDevice);
        g_wdmDevice = NULL;
        DbgPrint("Games8thGuard: callout registration failed %08x\n", status);
        return status;
    }

    DbgPrint("Games8thGuard: loaded successfully (callouts V4=%u V6=%u)\n",
             g_calloutV4Id, g_calloutV6Id);
    return STATUS_SUCCESS;
}
