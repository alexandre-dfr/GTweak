try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SilentInstalledAppsEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SystemPaneSuggestionsEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SoftLandingEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-338388Enabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-338389Enabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-338393Enabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-353694Enabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'SubscribedContent-353696Enabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'OemPreInstalledAppsEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'PreInstalledAppsEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager', 'PreInstalledAppsEverEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableWindowsConsumerFeatures', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableConsumerAccountStateContent', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent', 'DisableSoftLanding', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'ShowSyncProviderNotifications', 'DWord', 0)
    )
} catch {
    'Unknown'
}
