Reset-GTTweaks -Registry @(
    @('HKCU:\Environment', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'String', $null),
    @('HKCU:\Environment', 'POWERSHELL_TELEMETRY_OPTOUT', 'String', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\SQM', 'OptIn', 'DWord', $null),
    @('HKCU:\Software\Microsoft\VisualStudio\Telemetry', 'TurnOffSwitch', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\Feedback', 'DisableFeedbackDialog', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\Feedback', 'DisableEmailInput', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\Feedback', 'DisableScreenshotCapture', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\IntelliCode', 'DisableRemoteAnalysis', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\MediaPlayer\Preferences', 'UsageTracking', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\WindowsMediaPlayer', 'PreventCDDVDMetadataRetrieval', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\WindowsMediaPlayer', 'PreventMusicFileMetadataRetrieval', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\WindowsMediaPlayer', 'PreventRadioPresetsRetrieval', 'DWord', $null)
) `
    -Service @(
        @('VSStandardCollectorService150', 'Manual'),
        @('WMPNetworkSvc', 'Manual'),
        @('Razer Game Scanner Service', 'Automatic'),
        @('LogiRegistryService', 'Automatic')
    )
