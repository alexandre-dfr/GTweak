Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'AITEnable', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'DisableUAR', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'DisableInventory', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'DisablePCA', 'DWord', $null)
) `
    -Service @(,@('PcaSvc', 'Automatic')) `
    -Task @(
        '\Microsoft\Windows\Application Experience\AitAgent',
        '\Microsoft\Windows\Application Experience\StartupAppTask',
        '\Microsoft\Windows\Application Experience\PcaPatchDbTask',
        '\Microsoft\Windows\Application Experience\SdbinstMergeDbTask',
        '\Microsoft\Windows\Application Experience\MareBackup'
    )
