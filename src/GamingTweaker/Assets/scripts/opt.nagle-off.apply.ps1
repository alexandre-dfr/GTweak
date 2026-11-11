$keys = @(Get-GTTcpInterfaceKeys)
if ($keys.Count -eq 0) { Skip-GTItem 'Aucune interface réseau IP détectée.' }
foreach ($k in $keys) {
    Set-TweakRegistryIfNeeded -Path $k -Name 'TcpAckFrequency' -Type DWord -Value 1
    Set-TweakRegistryIfNeeded -Path $k -Name 'TCPNoDelay' -Type DWord -Value 1
}
