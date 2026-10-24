$path = "C:\Windows\System32\wbiosrvc.dll"
if (Test-Path $path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $unicode = [System.Text.Encoding]::Unicode.GetString($bytes)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    Write-Host "Checking $path..."
    if ($unicode.Contains("Force Integrity") -or $ascii.Contains("Force Integrity")) {
        Write-Host "Contains 'Force Integrity'"
    }
    if ($unicode.Contains("LOAD_LIBRARY_REQUIRE_FORCE_INTEGRITY") -or $ascii.Contains("LOAD_LIBRARY_REQUIRE_FORCE_INTEGRITY")) {
        Write-Host "Contains 'LOAD_LIBRARY_REQUIRE_FORCE_INTEGRITY'"
    }
}
