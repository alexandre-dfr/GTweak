try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'AITEnable', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'DisableUAR', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'DisableInventory', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat', 'DisablePCA', 'DWord', 1)
    ) `
        -Service @(,@('PcaSvc', 'Manual')) `
        -Task @(
            '\Microsoft\Windows\Application Experience\AitAgent',
            '\Microsoft\Windows\Application Experience\StartupAppTask',
            '\Microsoft\Windows\Application Experience\PcaPatchDbTask',
            '\Microsoft\Windows\Application Experience\SdbinstMergeDbTask',
            '\Microsoft\Windows\Application Experience\MareBackup'
        )
} catch {
    'Unknown'
}
