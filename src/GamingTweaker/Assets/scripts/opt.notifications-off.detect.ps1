try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Software\Policies\Microsoft\Windows\Explorer', 'DisableNotificationCenter', 'DWord', 1),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications', 'ToastEnabled', 'DWord', 0)
    )
} catch {
    'Unknown'
}
