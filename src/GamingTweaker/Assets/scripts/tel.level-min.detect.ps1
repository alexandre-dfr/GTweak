try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'AllowTelemetry', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'AllowCommercialDataPipeline', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'DisableOneSettingsDownloads', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection', 'DoNotShowFeedbackNotifications', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection', 'AllowTelemetry', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform', 'NoGenTicket', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy', 'TailoredExperiencesWithDiagnosticDataEnabled', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Siuf\Rules', 'NumberOfSIUFInPeriod', 'DWord', 0)
    )
} catch {
    'Unknown'
}
