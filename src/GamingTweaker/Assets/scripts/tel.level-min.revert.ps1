Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'AllowTelemetry', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'AllowCommercialDataPipeline', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'DisableOneSettingsDownloads', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'DoNotShowFeedbackNotifications', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection', 'AllowTelemetry', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform', 'NoGenTicket', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy', 'TailoredExperiencesWithDiagnosticDataEnabled', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Siuf\Rules', 'NumberOfSIUFInPeriod', 'DWord', $null)
)
