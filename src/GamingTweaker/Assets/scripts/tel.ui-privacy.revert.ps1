Reset-GTTweaks -Registry @(
    @('HKCU:\Software\Policies\Microsoft\Windows\EdgeUI', 'DisableMFUTracking', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\Windows\EdgeUI', 'DisableRecentApps', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\Windows\EdgeUI', 'TurnOffBackstack', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'DisableLockScreenAppNotifications', 'DWord', $null)
)
