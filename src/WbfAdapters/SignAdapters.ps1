param(
    [string]$TargetDir = ""
)

Write-Host "=== Mantra WBF Adapter Signing Tool ==="

# 1. Check or Create Code Signing Certificate
$certSubject = "CN=Mantra WBF Driver"
$cert = Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like "*$certSubject*" } | Select-Object -First 1
if (-not $cert) {
    $cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like "*$certSubject*" } | Select-Object -First 1
}

if (-not $cert) {
    Write-Host "[INFO] Creating new self-signed Code Signing Certificate..."
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject -CertStoreLocation "Cert:\CurrentUser\My" -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(10)
    Write-Host "[OK] Created Certificate Thumbprint: $($cert.Thumbprint)"
} else {
    Write-Host "[OK] Found existing Certificate Thumbprint: $($cert.Thumbprint)"
}

# 2. Export public certificate (.cer)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Resolve-Path (Join-Path $scriptDir "..\..\dist\MFS100_Deployment") -ErrorAction SilentlyContinue
if (-not $distDir) {
    $distDir = Join-Path $scriptDir "..\..\dist\MFS100_Deployment"
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

$cerPath = Join-Path $distDir "MantraWbfCert.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "[OK] Exported Certificate to: $cerPath"

# 3. Sign target files non-interactively
# (The public certificate MantraWbfCert.cer is installed into LocalMachine\Root by 7_Register_WBF_Windows_Hello.bat)

# 4. Sign target files
$targets = @(
    (Join-Path $scriptDir "MFS100SensorAdapter.dll"),
    (Join-Path $scriptDir "MFS100EngineAdapter.dll"),
    (Join-Path $distDir "MFS100SensorAdapter.dll"),
    (Join-Path $distDir "MFS100EngineAdapter.dll")
)

if ($TargetDir -and (Test-Path $TargetDir)) {
    $targets += (Join-Path $TargetDir "MFS100SensorAdapter.dll")
    $targets += (Join-Path $TargetDir "MFS100EngineAdapter.dll")
}

foreach ($target in $targets) {
    if (Test-Path $target) {
        $sig = Set-AuthenticodeSignature -FilePath $target -Certificate $cert -HashAlgorithm SHA256
        Write-Host "[SIGNED] $($target): $($sig.Status)"
    }
}

Write-Host "=== Signing Complete ==="
