$k = 'HKCU:\Environment'
Set-TweakRegistryIfNeeded -Path $k -Name 'DOTNET_CLI_TELEMETRY_OPTOUT' -Type String -Value '1'
Set-TweakRegistryIfNeeded -Path $k -Name 'POWERSHELL_TELEMETRY_OPTOUT' -Type String -Value '1'
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\SQM'
Set-TweakRegistryIfNeeded -Path $k -Name 'OptIn' -Type DWord -Value 0
$k = 'HKCU:\Software\Microsoft\VisualStudio\Telemetry'
Set-TweakRegistryIfNeeded -Path $k -Name 'TurnOffSwitch' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\Feedback'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableFeedbackDialog' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableEmailInput' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableScreenshotCapture' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\VisualStudio\IntelliCode'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableRemoteAnalysis' -Type DWord -Value 1
$k = 'HKCU:\SOFTWARE\Microsoft\MediaPlayer\Preferences'
Set-TweakRegistryIfNeeded -Path $k -Name 'UsageTracking' -Type DWord -Value 0
$k = 'HKCU:\Software\Policies\Microsoft\WindowsMediaPlayer'
Set-TweakRegistryIfNeeded -Path $k -Name 'PreventCDDVDMetadataRetrieval' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'PreventMusicFileMetadataRetrieval' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'PreventRadioPresetsRetrieval' -Type DWord -Value 1
[void](Set-TweakServiceIfNeeded -Name 'VSStandardCollectorService150' -StartType 'Manual')
[void](Set-TweakServiceIfNeeded -Name 'WMPNetworkSvc' -StartType 'Manual')
[void](Set-TweakServiceIfNeeded -Name 'Razer Game Scanner Service' -StartType 'Manual')
[void](Set-TweakServiceIfNeeded -Name 'LogiRegistryService' -StartType 'Manual')
