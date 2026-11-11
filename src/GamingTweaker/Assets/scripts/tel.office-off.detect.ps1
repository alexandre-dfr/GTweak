try {
    Get-GTTweakState -Registry @(
        @('HKCU:\SOFTWARE\Microsoft\Office\Common\ClientTelemetry', 'DisableTelemetry', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\Office\Common\ClientTelemetry', 'VerboseLogging', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\ClientTelemetry', 'DisableTelemetry', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\ClientTelemetry', 'VerboseLogging', 'DWord', 0),
        @('HKCU:\SOFTWARE\Policies\Microsoft\Office\15.0\OSM', 'EnableLogging', 'DWord', 0),
        @('HKCU:\SOFTWARE\Policies\Microsoft\Office\15.0\OSM', 'EnableUpload', 'DWord', 0),
        @('HKCU:\Software\Policies\Microsoft\Office\15.0\Common', 'QMEnable', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\Feedback', 'Enabled', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\ClientTelemetry', 'DisableTelemetry', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\ClientTelemetry', 'VerboseLogging', 'DWord', 0),
        @('HKCU:\SOFTWARE\Policies\Microsoft\Office\16.0\OSM', 'EnableLogging', 'DWord', 0),
        @('HKCU:\SOFTWARE\Policies\Microsoft\Office\16.0\OSM', 'EnableUpload', 'DWord', 0),
        @('HKCU:\Software\Policies\Microsoft\Office\16.0\Common', 'QMEnable', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\Feedback', 'Enabled', 'DWord', 0)
    ) `
        -Task @(
            '\Microsoft\Office\OfficeTelemetryAgentFallBack',
            '\Microsoft\Office\OfficeTelemetryAgentFallBack2016',
            '\Microsoft\Office\OfficeTelemetryAgentLogOn',
            '\Microsoft\Office\OfficeTelemetryAgentLogOn2016'
        )
} catch {
    'Unknown'
}
