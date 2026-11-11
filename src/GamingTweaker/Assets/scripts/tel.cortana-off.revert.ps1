Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowCortana', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowCloudSearch', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowCortanaAboveLock', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'CortanaConsent', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'CanCortanaBeEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'HistoryViewEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'DeviceHistoryEnabled', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'VoiceShortcut', 'DWord', 1),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search', 'CortanaEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search', 'CortanaInAmbientMode', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'ShowCortanaButton', 'DWord', 1),
    @('HKCU:\Software\Microsoft\Speech_OneCore\Preferences', 'VoiceActivationOn', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Speech_OneCore\Preferences', 'VoiceActivationEnableAboveLockscreen', 'DWord', $null),
    @('HKLM:\Software\Microsoft\Speech_OneCore\Preferences', 'VoiceActivationDefaultOn', 'DWord', $null)
)
