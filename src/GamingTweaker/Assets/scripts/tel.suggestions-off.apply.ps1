$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
Set-TweakRegistryIfNeeded -Path $k -Name 'SilentInstalledAppsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SystemPaneSuggestionsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SoftLandingEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SubscribedContent-338388Enabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SubscribedContent-338389Enabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SubscribedContent-338393Enabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SubscribedContent-353694Enabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SubscribedContent-353696Enabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'OemPreInstalledAppsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'PreInstalledAppsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'PreInstalledAppsEverEnabled' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableWindowsConsumerFeatures' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableSoftLanding' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableConsumerAccountStateContent' -Type DWord -Value 1
$k = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
Set-TweakRegistryIfNeeded -Path $k -Name 'ShowSyncProviderNotifications' -Type DWord -Value 0
