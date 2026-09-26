[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$Sys,
    [Parameter(Mandatory = $true)] [string]$App,
    [Parameter(Mandatory = $true)] [string]$Log,
    [string]$Verifier = ''
)

$ErrorActionPreference = 'Stop'
$service = 'Games8thGuardNativeLoadTest'
$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$service"
$registryPath = "\Registry\Machine\System\CurrentControlSet\Services\$service"
$lines = New-Object System.Collections.Generic.List[string]
$ntLoadSucceeded = $false
$overall = 1

function Add-Log([string]$Text) {
    $line = '[{0:HH:mm:ss.fff}] {1}' -f (Get-Date), $Text
    [void]$lines.Add($line)
    Write-Host $Text
}

if (-not ('NativeDriverLoader' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeDriverLoader {
    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] public struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool LookupPrivilegeValue(string SystemName, string Name, ref LUID Luid);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr Handle);
    public static bool EnableLoadDriverPrivilege(out int error) {
        error = 0; IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x20u | 0x8u, out token)) { error = Marshal.GetLastWin32Error(); return false; }
        try {
            var luid = new LUID();
            if (!LookupPrivilegeValue(null, "SeLoadDriverPrivilege", ref luid)) { error = Marshal.GetLastWin32Error(); return false; }
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = 0x2u } };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) { error = Marshal.GetLastWin32Error(); return false; }
            error = Marshal.GetLastWin32Error(); return error == 0;
        } finally { CloseHandle(token); }
    }
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    public static extern int NtLoadDriver(ref UNICODE_STRING DriverServiceName);
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    public static extern int NtUnloadDriver(ref UNICODE_STRING DriverServiceName);
    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }
    public static int Load(string value) {
        var s = new UNICODE_STRING();
        s.Length = (ushort)(value.Length * 2);
        s.MaximumLength = (ushort)(s.Length + 2);
        s.Buffer = Marshal.StringToHGlobalUni(value);
        try { return NtLoadDriver(ref s); } finally { Marshal.FreeHGlobal(s.Buffer); }
    }
    public static int Unload(string value) {
        var s = new UNICODE_STRING();
        s.Length = (ushort)(value.Length * 2);
        s.MaximumLength = (ushort)(s.Length + 2);
        s.Buffer = Marshal.StringToHGlobalUni(value);
        try { return NtUnloadDriver(ref s); } finally { Marshal.FreeHGlobal(s.Buffer); }
    }
}
'@
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    Add-Log ('ELEVATED=' + $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'ADMIN_REQUIRED' }
    $privilegeError = 0
    $privilegeEnabled = [NativeDriverLoader]::EnableLoadDriverPrivilege([ref]$privilegeError)
    Add-Log ('SE_LOAD_DRIVER_PRIVILEGE_ENABLED=' + $privilegeEnabled + ';ERROR=' + $privilegeError)
    if (-not $privilegeEnabled) { throw "SE_LOAD_DRIVER_PRIVILEGE_FAILED=$privilegeError" }
    if (-not (Test-Path -LiteralPath $Sys)) { throw "SYS_NOT_FOUND=$Sys" }
    Add-Log ('SYS=' + $Sys)
    Add-Log ('SYS_SHA256=' + (Get-FileHash -LiteralPath $Sys -Algorithm SHA256).Hash.ToLowerInvariant())
    $signature = Get-AuthenticodeSignature -LiteralPath $Sys
    Add-Log ('AUTHENTICODE_STATUS=' + $signature.Status)
    Add-Log ('AUTHENTICODE_MESSAGE=' + $signature.StatusMessage)
    if ($signature.SignerCertificate) { Add-Log ('SIGNER=' + $signature.SignerCertificate.Subject) }

    if (Test-Path $serviceKey) { throw "ABORT_SERVICE_KEY_ALREADY_EXISTS=$service" }
    New-Item -Path $serviceKey -Force | Out-Null
    New-ItemProperty -Path $serviceKey -Name Type -PropertyType DWord -Value 1 -Force | Out-Null
    New-ItemProperty -Path $serviceKey -Name Start -PropertyType DWord -Value 3 -Force | Out-Null
    New-ItemProperty -Path $serviceKey -Name ErrorControl -PropertyType DWord -Value 1 -Force | Out-Null
    $nativeImagePath = '\??\' + [IO.Path]::GetFullPath($Sys)
    New-ItemProperty -Path $serviceKey -Name ImagePath -PropertyType String -Value $nativeImagePath -Force | Out-Null
    Add-Log ('REGISTRY_PATH=' + $registryPath)
    Add-Log ('IMAGE_PATH=' + $nativeImagePath)

    $status = [NativeDriverLoader]::Load($registryPath)
    $statusHex = '0x' + ([int32]$status).ToString('X8')
    Add-Log ('NTLOADDRIVER_STATUS=' + $statusHex)
    if ($status -eq 0) {
        $ntLoadSucceeded = $true
        Start-Sleep -Milliseconds 500
        if (-not [string]::IsNullOrWhiteSpace($Verifier)) {
            Add-Log ('> ' + $Verifier)
            $verify = & $Verifier 2>&1
            $verifyCode = $LASTEXITCODE
        } else {
            Add-Log ('> ' + $App + ' cli driver-status')
            $verify = & $App cli driver-status 2>&1
            $verifyCode = $LASTEXITCODE
        }
        foreach ($item in $verify) { Add-Log ([string]$item) }
        Add-Log ('DRIVER_STATUS_EXIT=' + $verifyCode)
        $overall = if ($verifyCode -eq 0) { 0 } else { 23 }
    } else {
        $overall = 24
    }
}
catch {
    Add-Log ('EXCEPTION=' + $_.Exception.Message)
    $overall = 25
}
finally {
    Add-Log '--- CLEANUP ---'
    if ($ntLoadSucceeded) {
        try {
            $unload = [NativeDriverLoader]::Unload($registryPath)
            Add-Log ('NTUNLOADDRIVER_STATUS=0x' + ([int32]$unload).ToString('X8'))
        } catch { Add-Log ('UNLOAD_EXCEPTION=' + $_.Exception.Message); $overall = 26 }
    }
    if (Test-Path $serviceKey) {
        Remove-Item -LiteralPath $serviceKey -Recurse -Force -ErrorAction SilentlyContinue
        Add-Log ('REGISTRY_REMOVED=' + (-not (Test-Path $serviceKey)))
    }
    Add-Log ('FINAL_EXIT=' + $overall)
    [IO.File]::WriteAllLines($Log, $lines, [Text.UTF8Encoding]::new($false))
}
exit $overall
