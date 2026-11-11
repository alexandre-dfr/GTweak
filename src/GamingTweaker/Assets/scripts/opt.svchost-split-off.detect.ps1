try {
    $ram = (Get-CimInstance Win32_PhysicalMemory | Measure-Object -Property Capacity -Sum).Sum
    if (-not $ram) { throw 'mémoire illisible' }
    $kb = [int64][math]::Ceiling($ram / 1KB)
    $cur = Get-GTRegValue 'HKLM:\SYSTEM\CurrentControlSet\Control' 'SvcHostSplitThresholdInKB'
    if ($cur.Existed -and $cur.Type -eq 'DWord' -and ([int64]$cur.Value -ge $kb)) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
