Reset-GTTweaks -Registry @(
    @('HKCU:\SOFTWARE\Microsoft\Office\Common\ClientTelemetry', 'DisableTelemetry', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Office\Common\ClientTelemetry', 'VerboseLogging', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\ClientTelemetry', 'DisableTelemetry', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\ClientTelemetry', 'VerboseLogging', 'DWord', $null),
    @('HKCU:\SOFTWARE\Policies\Microsoft\Office\15.0\OSM', 'EnableLogging', 'DWord', $null),
    @('HKCU:\SOFTWARE\Policies\Microsoft\Office\15.0\OSM', 'EnableUpload', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\Office\15.0\Common', 'QMEnable', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\Feedback', 'Enabled', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\ClientTelemetry', 'DisableTelemetry', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\ClientTelemetry', 'VerboseLogging', 'DWord', $null),
    @('HKCU:\SOFTWARE\Policies\Microsoft\Office\16.0\OSM', 'EnableLogging', 'DWord', $null),
    @('HKCU:\SOFTWARE\Policies\Microsoft\Office\16.0\OSM', 'EnableUpload', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\Office\16.0\Common', 'QMEnable', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\Feedback', 'Enabled', 'DWord', $null)
) `
    -Task @(
        '\Microsoft\Office\OfficeTelemetryAgentFallBack',
        '\Microsoft\Office\OfficeTelemetryAgentFallBack2016',
        '\Microsoft\Office\OfficeTelemetryAgentLogOn',
        '\Microsoft\Office\OfficeTelemetryAgentLogOn2016'
    )
