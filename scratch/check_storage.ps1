$path = "C:\Windows\System32\WinBioPlugIns\winbiostorageadapter.dll"
$bytes = [System.IO.File]::ReadAllBytes($path)
$u = [System.Text.Encoding]::Unicode.GetString($bytes)
$a = [System.Text.Encoding]::ASCII.GetString($bytes)

foreach ($word in @("AutoName", "FilePath", "ConnectionString", "AutoCreate", "InitialSize", "Format", "WinBioDatabase")) {
    $foundU = $u.Contains($word)
    $foundA = $a.Contains($word)
    Write-Host "$word : Unicode=$foundU, ASCII=$foundA"
}
