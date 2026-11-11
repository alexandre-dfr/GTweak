Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'DisableSettingSync', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'DisableSettingSyncUserOverride', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'DisableSyncOnPaidNetwork', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync', 'SyncPolicy', 'DWord', $null)
)
