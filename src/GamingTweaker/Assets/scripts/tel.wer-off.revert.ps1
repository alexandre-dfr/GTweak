Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting', 'Disabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting', 'DontSendAdditionalData', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting', 'LoggingDisabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting', 'Disabled', 'DWord', $null)
) `
    -Service @(,@('WerSvc', 'Manual')) `
    -Task @(
        '\Microsoft\Windows\Windows Error Reporting\QueueReporting'
    )
