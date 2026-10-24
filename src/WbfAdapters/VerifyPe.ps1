$files = @(
    "dist\MFS100_Deployment\MFS100SensorAdapter.dll",
    "dist\MFS100_Deployment\MFS100EngineAdapter.dll"
)

foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $file))
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $dllCharsOffset = $peOffset + 4 + 20 + 70
    $dllChars = [BitConverter]::ToUInt16($bytes, $dllCharsOffset)
    $hasForceIntegrity = ($dllChars -band 0x0080) -ne 0
    $sig = Get-AuthenticodeSignature -FilePath $file
    Write-Host "$file :"
    Write-Host "  DllCharacteristics: 0x$($dllChars.ToString('X4'))"
    Write-Host "  ForceIntegrity: $hasForceIntegrity"
    Write-Host "  Signed: $(($sig.SignerCertificate -ne $null))"
    Write-Host "  Signer: $($sig.SignerCertificate.Subject)"
    Write-Host "  Thumbprint: $($sig.SignerCertificate.Thumbprint)"
    Write-Host "  Status: $($sig.Status)"
}
