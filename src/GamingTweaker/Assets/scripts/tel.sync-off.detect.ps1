try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'DisableSettingSync', 'DWord', 2),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'DisableSettingSyncUserOverride', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'DisableSyncOnPaidNetwork', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'SyncPolicy', 'DWord', 5)
    )
} catch {
    'Unknown'
}
