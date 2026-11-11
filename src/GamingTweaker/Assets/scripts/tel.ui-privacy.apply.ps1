$k = 'HKCU:\Software\Policies\Microsoft\Windows\EdgeUI'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableMFUTracking' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableRecentApps' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'TurnOffBackstack' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableLockScreenAppNotifications' -Type DWord -Value 1
