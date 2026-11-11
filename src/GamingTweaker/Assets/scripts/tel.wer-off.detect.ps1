try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting', 'Disabled', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting', 'DontSendAdditionalData', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting', 'LoggingDisabled', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting', 'Disabled', 'DWord', 1)
    ) `
        -Service @(,@('WerSvc', 'Disabled')) `
        -Task @(
            '\Microsoft\Windows\Windows Error Reporting\QueueReporting'
        )
} catch {
    'Unknown'
}
