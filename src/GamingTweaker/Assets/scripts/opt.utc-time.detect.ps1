try {
    $cur = Get-GTRegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\TimeZoneInformation' 'RealTimeIsUniversal'
    if ($cur.Existed -and (($cur.Type -eq 'QWord') -or ($cur.Type -eq 'DWord')) -and ([int64]$cur.Value -eq 1)) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
