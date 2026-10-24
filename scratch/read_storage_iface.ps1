$lines = Get-Content 'C:\Program Files (x86)\Windows Kits\10\Include\10.0.26100.0\um\winbio_adapter.h'
for ($i = 3580; $i -le 3660; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}
