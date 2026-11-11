$k = 'HKCU:\Control Panel\Mouse'
try {
    $p = Get-ItemProperty -Path $k -ErrorAction Stop
    if ($p.MouseSpeed -eq '0' -and $p.MouseThreshold1 -eq '0' -and $p.MouseThreshold2 -eq '0') { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
