Reset-GTTweaks -Registry @(
    @('HKCU:\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy', 'HasAccepted', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Personalization\Settings', 'AcceptedPrivacyPolicy', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\InputPersonalization', 'RestrictImplicitInkCollection', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\InputPersonalization', 'RestrictImplicitTextCollection', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\InputPersonalization', 'AllowInputPersonalization', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports', 'PreventHandwritingErrorReports', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\TabletPC', 'PreventHandwritingDataSharing', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore', 'HarvestContacts', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Input\TIPC', 'Enabled', 'DWord', 1),
    @('HKLM:\SOFTWARE\Microsoft\Input\TIPC', 'Enabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors', 'DisableSensors', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config', 'AutoConnectAllowedOEM', 'DWord', 1)
)
