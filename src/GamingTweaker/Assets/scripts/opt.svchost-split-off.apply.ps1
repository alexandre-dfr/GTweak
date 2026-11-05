$k = 'HKLM:\SYSTEM\CurrentControlSet\Control'
$ram = (Get-CimInstance Win32_PhysicalMemory | Measure-Object -Property Capacity -Sum).Sum
if (-not $ram) { throw 'La quantité de mémoire vive est illisible : réglage non appliqué.' }
$kb = [int64][math]::Ceiling($ram / 1KB)
if ($kb -gt 4294967295) { $kb = 4294967295 }
$cur = Get-GTRegValue $k 'SvcHostSplitThresholdInKB'
if (-not ($cur.Existed -and $cur.Type -eq 'DWord' -and ([int64]$cur.Value -ge $kb))) {
    Set-TweakRegistryIfNeeded -Path $k -Name 'SvcHostSplitThresholdInKB' -Type DWord -Value $kb
}
