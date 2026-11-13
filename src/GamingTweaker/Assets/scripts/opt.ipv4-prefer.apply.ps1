$k = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'
$cur = Get-GTRegValue $k 'DisabledComponents'
$v = 0
if ($cur.Existed -and $cur.Type -eq 'DWord') { $v = [int64]$cur.Value }
if (($v -band 0x20) -ne 0x20) { Set-TweakRegistryIfNeeded -Path $k -Name 'DisabledComponents' -Type DWord -Value ($v -bor 0x20) }
