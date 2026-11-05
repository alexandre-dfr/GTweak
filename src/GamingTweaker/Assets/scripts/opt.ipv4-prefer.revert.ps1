$k = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'
$cur = Get-GTRegValue $k 'DisabledComponents'
if ($cur.Existed -and $cur.Type -eq 'DWord') {
    $v = [int64]$cur.Value
    if ($v -eq 32) { Reset-GTTweaks -Registry @(,@($k, 'DisabledComponents', 'DWord', $null)) }
    elseif (($v -ne 255) -and (($v -band 0x20) -eq 0x20)) { Set-GTRegRaw $k 'DisabledComponents' 'DWord' (ConvertTo-GTRegData 'DWord' ($v -band (-bnot 0x20))) }
}
