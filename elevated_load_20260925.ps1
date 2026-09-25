param(
  [ValidateSet('dse', 'testsigning')]
  [string]$Mode = 'dse',
  [int]$Provider = 4,
  [string]$ServiceDriverPath = '',
  [switch]$KeepTestSigning,
  [switch]$LeaveRunning
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $root 'driver_load_20260925.log'
$sys = Join-Path $root 'driver\build\Release\Games8thGuard.sys'
$serviceSys = if ([string]::IsNullOrWhiteSpace($ServiceDriverPath)) {
  $sys
} else {
  [IO.Path]::GetFullPath($ServiceDriverPath)
}
$nativeServiceSys = if ($serviceSys.StartsWith('\??\')) {
  $serviceSys
} else {
  '\??\' + $serviceSys
}
$kdu = Join-Path $root 'tools\kdu\Source\Hamakaze\output\x64\Release\kdu.exe'

"=== $(Get-Date -Format s) ===" | Set-Content -LiteralPath $log

function Run($label, [scriptblock]$action) {
  "[$label]" | Add-Content -LiteralPath $log
  & $action 2>&1 | Tee-Object -FilePath $log -Append | Out-Host
  $code = $LASTEXITCODE
  "EXIT=$code" | Add-Content -LiteralPath $log
  return $code
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  'ERROR=Administrator token required' | Add-Content -LiteralPath $log
  exit 740
}

if (-not (Test-Path -LiteralPath $serviceSys)) {
  "MISSING_DRIVER=$serviceSys" | Add-Content -LiteralPath $log
  exit 2
}

if ($Mode -eq 'dse' -and -not (Test-Path -LiteralPath $kdu)) {
  "MISSING_KDU=$kdu" | Add-Content -LiteralPath $log
  exit 3
}

Get-AuthenticodeSignature -LiteralPath $serviceSys |
  Format-List * | Tee-Object -FilePath $log -Append

Run 'service stop' { sc.exe stop Games8thGuard }
Run 'service delete' { sc.exe delete Games8thGuard }

$dseChanged = $false
$testSigningChanged = $false
try {
  if ($Mode -eq 'dse') {
    $dseCode = Run "kdu dse disable provider $Provider" {
      & $kdu -prv $Provider -dse 0
    }
    $dseFailure = (Get-Content -LiteralPath $log -Raw) -replace "`0", ''
    if ($dseCode -ne 0 -or
        $dseFailure -match 'Driver resource id cannot be found|Cannot query DSE state|Unable to open vulnerable driver|DSE patch') {
      "DSE_DISABLE_FAILED=$dseCode" | Add-Content -LiteralPath $log
      exit 4
    }
    $dseChanged = $true
  }
  else {
    $testCode = Run 'bcdedit testsigning on' { bcdedit.exe /set testsigning on }
    if ($testCode -ne 0) {
      "TESTSIGNING_ENABLE_FAILED=$testCode" | Add-Content -LiteralPath $log
      exit 5
    }
    $testSigningChanged = $true
  }

  Run 'service create' {
    sc.exe create Games8thGuard type= kernel start= demand "binPath= `"$serviceSys`""
  }
  Run 'service config' {
    sc.exe config Games8thGuard type= kernel start= demand "binPath= `"$serviceSys`""
  }
  Run 'service image path fix' {
    $serviceKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\Games8thGuard'
    Set-ItemProperty -LiteralPath $serviceKey -Name ImagePath -Value $nativeServiceSys
  }
  Run 'service query config' { sc.exe qc Games8thGuard }
  $startCode = Run 'service start' { sc.exe start Games8thGuard }
  if ($startCode -ne 0) {
    "SERVICE_START_FAILED=$startCode" | Add-Content -LiteralPath $log
    exit 6
  }
  Run 'service query' { sc.exe query Games8thGuard }

  $svc = Get-CimInstance Win32_SystemDriver -Filter "Name='Games8thGuard'" -ErrorAction SilentlyContinue
  if ($svc) {
    $svc | Format-List Name,State,StartMode,PathName |
      Tee-Object -FilePath $log -Append
  }

  if ($dseChanged) {
    Run "kdu dse restore provider $Provider" {
      & $kdu -prv $Provider -dse 6
    }
  }

  if (-not $LeaveRunning) {
    Run 'service stop cleanup' { sc.exe stop Games8thGuard }
    Run 'service delete cleanup' { sc.exe delete Games8thGuard }
  }
}
finally {
  if ($dseChanged) {
    Run "kdu dse restore finally provider $Provider" {
      & $kdu -prv $Provider -dse 6
    }
  }
  if ($testSigningChanged -and -not $KeepTestSigning) {
    Run 'bcdedit testsigning off cleanup' { bcdedit.exe /set testsigning off }
  }
  "=== END $(Get-Date -Format s) ===" | Add-Content -LiteralPath $log
}
