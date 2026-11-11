$gtRoots = @(
    (Join-Path $env:APPDATA 'Sun\Java\Deployment\cache'),
    (Join-Path $env:USERPROFILE '.dotnet\TelemetryStorageService'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\VSApplicationInsights'),
    (Join-Path $env:ProgramData 'Microsoft\VSApplicationInsights'),
    (Join-Path $env:APPDATA 'vstelemetry'),
    (Join-Path $env:ProgramData 'vstelemetry')
)
foreach ($gtV in @('14.0', '15.0', '16.0', '17.0')) { $gtRoots += (Join-Path $env:LOCALAPPDATA ('Microsoft\VSCommon\' + $gtV + '\SQM')) }
foreach ($gtT in @('Microsoft\VSApplicationInsights', 'VSFaultInfo', 'VSFeedbackPerfWatsonData', 'VSFeedbackVSRTCLogs', 'VSFeedbackIntelliCodeLogs',
                   'VSRemoteControl', 'Microsoft\VSFeedbackCollector', 'VSTelem', 'VSTelem.Out')) { $gtRoots += (Join-Path $env:TEMP $gtT) }
Get-TweakPathSize -Path $gtRoots
