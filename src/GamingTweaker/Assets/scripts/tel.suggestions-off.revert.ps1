Reset-GTTweaks -Registry @(
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SilentInstalledAppsEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SystemPaneSuggestionsEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SoftLandingEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-338388Enabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-338389Enabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-338393Enabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-353694Enabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-353696Enabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'OemPreInstalledAppsEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'PreInstalledAppsEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'PreInstalledAppsEverEnabled', 'DWord', 1),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableWindowsConsumerFeatures', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableConsumerAccountStateContent', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableSoftLanding', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'ShowSyncProviderNotifications', 'DWord', 1)
)
