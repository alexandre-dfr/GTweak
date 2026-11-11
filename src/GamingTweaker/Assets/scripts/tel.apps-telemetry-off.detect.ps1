try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Environment', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'String', '1'),
        @('HKCU:\Environment', 'POWERSHELL_TELEMETRY_OPTOUT', 'String', '1'),
        @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\SQM', 'OptIn', 'DWord', 0),
        @('HKCU:\Software\Microsoft\VisualStudio\Telemetry', 'TurnOffSwitch', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\Feedback', 'DisableFeedbackDialog', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\Feedback', 'DisableEmailInput', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\Feedback', 'DisableScreenshotCapture', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\IntelliCode', 'DisableRemoteAnalysis', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\MediaPlayer\Preferences', 'UsageTracking', 'DWord', 0),
        @('HKCU:\Software\Policies\Microsoft\WindowsMediaPlayer', 'PreventCDDVDMetadataRetrieval', 'DWord', 1),
        @('HKCU:\Software\Policies\Microsoft\WindowsMediaPlayer', 'PreventMusicFileMetadataRetrieval', 'DWord', 1),
        @('HKCU:\Software\Policies\Microsoft\WindowsMediaPlayer', 'PreventRadioPresetsRetrieval', 'DWord', 1)
    ) `
        -Service @(
            @('VSStandardCollectorService150', 'Manual'),
            @('WMPNetworkSvc', 'Manual'),
            @('Razer Game Scanner Service', 'Manual'),
            @('LogiRegistryService', 'Manual')
        )
} catch {
    'Unknown'
}
