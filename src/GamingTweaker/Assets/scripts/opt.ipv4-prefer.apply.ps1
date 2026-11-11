$k = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters'
if ($GT_SelectedIds -contains 'opt.ipv6-off') { Skip-GTItem 'IPv6 est désactivé par l''option « IPv6 désactivé » : la préférence pour IPv4 est inutile.' }
$cur = Get-GTRegValue $k 'DisabledComponents'
$v = 0
if ($cur.Existed -and $cur.Type -eq 'DWord') { $v = [int64]$cur.Value }
if (($v -band 0x20) -ne 0x20) { Set-TweakRegistryIfNeeded -Path $k -Name 'DisabledComponents' -Type DWord -Value ($v -bor 0x20) }
