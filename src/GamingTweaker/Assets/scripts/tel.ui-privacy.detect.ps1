try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Software\Policies\Microsoft\Windows\EdgeUI', 'DisableMFUTracking', 'DWord', 1),
        @('HKCU:\Software\Policies\Microsoft\Windows\EdgeUI', 'DisableRecentApps', 'DWord', 1),
        @('HKCU:\Software\Policies\Microsoft\Windows\EdgeUI', 'TurnOffBackstack', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'DisableLockScreenAppNotifications', 'DWord', 1)
    )
} catch {
    'Unknown'
}
