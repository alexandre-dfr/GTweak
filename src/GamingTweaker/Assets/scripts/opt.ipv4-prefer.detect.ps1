try {
    $cur = Get-GTRegValue 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters' 'DisabledComponents'
    if ($cur.Existed -and $cur.Type -eq 'DWord' -and (([int64]$cur.Value -band 0x20) -eq 0x20)) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
