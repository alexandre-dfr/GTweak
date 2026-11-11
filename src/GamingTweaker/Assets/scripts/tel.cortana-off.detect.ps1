try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowCortana', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowCloudSearch', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowCortanaAboveLock', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'CortanaConsent', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'CanCortanaBeEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'HistoryViewEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'DeviceHistoryEnabled', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'VoiceShortcut', 'DWord', 0),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search', 'CortanaEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search', 'CortanaInAmbientMode', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'ShowCortanaButton', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Speech_OneCore\Preferences', 'VoiceActivationOn', 'DWord', 0),
        @('HKCU:\Software\Microsoft\Speech_OneCore\Preferences', 'VoiceActivationEnableAboveLockscreen', 'DWord', 0),
        @('HKLM:\Software\Microsoft\Speech_OneCore\Preferences', 'VoiceActivationDefaultOn', 'DWord', 0)
    )
} catch {
    'Unknown'
}
