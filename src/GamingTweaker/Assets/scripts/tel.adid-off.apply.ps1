$k = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo'
Set-TweakRegistryIfNeeded -Path $k -Name 'Enabled' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisabledByGroupPolicy' -Type DWord -Value 1
