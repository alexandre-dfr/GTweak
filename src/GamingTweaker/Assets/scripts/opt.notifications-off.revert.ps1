Reset-GTTweaks -Registry @(
    @('HKCU:\Software\Policies\Microsoft\Windows\Explorer', 'DisableNotificationCenter', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\PushNotifications', 'ToastEnabled', 'DWord', 1)
)
