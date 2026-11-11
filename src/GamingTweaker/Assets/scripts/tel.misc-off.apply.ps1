$k = 'HKCU:\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy'
Set-TweakRegistryIfNeeded -Path $k -Name 'HasAccepted' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Microsoft\Personalization\Settings'
Set-TweakRegistryIfNeeded -Path $k -Name 'AcceptedPrivacyPolicy' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\InputPersonalization'
Set-TweakRegistryIfNeeded -Path $k -Name 'RestrictImplicitInkCollection' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'RestrictImplicitTextCollection' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowInputPersonalization' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports'
Set-TweakRegistryIfNeeded -Path $k -Name 'PreventHandwritingErrorReports' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\TabletPC'
Set-TweakRegistryIfNeeded -Path $k -Name 'PreventHandwritingDataSharing' -Type DWord -Value 1
$k = 'HKCU:\SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore'
Set-TweakRegistryIfNeeded -Path $k -Name 'HarvestContacts' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Microsoft\Input\TIPC'
Set-TweakRegistryIfNeeded -Path $k -Name 'Enabled' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Microsoft\Input\TIPC'
Set-TweakRegistryIfNeeded -Path $k -Name 'Enabled' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableSensors' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config'
Set-TweakRegistryIfNeeded -Path $k -Name 'AutoConnectAllowedOEM' -Type DWord -Value 0
