$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowCortana' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowCloudSearch' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowCortanaAboveLock' -Type DWord -Value 0
$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'
Set-TweakRegistryIfNeeded -Path $k -Name 'CortanaConsent' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'CanCortanaBeEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'HistoryViewEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'DeviceHistoryEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'VoiceShortcut' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search'
Set-TweakRegistryIfNeeded -Path $k -Name 'CortanaEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'CortanaInAmbientMode' -Type DWord -Value 0
$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
Set-TweakRegistryIfNeeded -Path $k -Name 'ShowCortanaButton' -Type DWord -Value 0
$k = 'HKCU:\Software\Microsoft\Speech_OneCore\Preferences'
Set-TweakRegistryIfNeeded -Path $k -Name 'VoiceActivationOn' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'VoiceActivationEnableAboveLockscreen' -Type DWord -Value 0
$k = 'HKLM:\Software\Microsoft\Speech_OneCore\Preferences'
Set-TweakRegistryIfNeeded -Path $k -Name 'VoiceActivationDefaultOn' -Type DWord -Value 0
