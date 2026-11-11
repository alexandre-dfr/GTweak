$k = 'HKCU:\SOFTWARE\Microsoft\Office\Common\ClientTelemetry'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableTelemetry' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'VerboseLogging' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\ClientTelemetry'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableTelemetry' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'VerboseLogging' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Policies\Microsoft\Office\15.0\OSM'
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableLogging' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableUpload' -Type DWord -Value 0
$k = 'HKCU:\Software\Policies\Microsoft\Office\15.0\Common'
Set-TweakRegistryIfNeeded -Path $k -Name 'QMEnable' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Microsoft\Office\15.0\Common\Feedback'
Set-TweakRegistryIfNeeded -Path $k -Name 'Enabled' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\ClientTelemetry'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableTelemetry' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'VerboseLogging' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Policies\Microsoft\Office\16.0\OSM'
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableLogging' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableUpload' -Type DWord -Value 0
$k = 'HKCU:\Software\Policies\Microsoft\Office\16.0\Common'
Set-TweakRegistryIfNeeded -Path $k -Name 'QMEnable' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Microsoft\Office\16.0\Common\Feedback'
Set-TweakRegistryIfNeeded -Path $k -Name 'Enabled' -Type DWord -Value 0
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Office\OfficeTelemetryAgentFallBack' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Office\OfficeTelemetryAgentFallBack2016' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Office\OfficeTelemetryAgentLogOn' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Office\OfficeTelemetryAgentLogOn2016' -Enabled $false)
