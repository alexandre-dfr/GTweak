$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableLocation' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableLocationScripting' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy'
Set-TweakRegistryIfNeeded -Path $k -Name 'LetAppsAccessLocation' -Type DWord -Value 2
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location'
Set-TweakRegistryIfNeeded -Path $k -Name 'Value' -Type String -Value 'Deny'
$k = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}'
Set-TweakRegistryIfNeeded -Path $k -Name 'SensorPermissionState' -Type DWord -Value 0
[void](Set-TweakServiceIfNeeded -Name 'lfsvc' -StartType 'Disabled')
