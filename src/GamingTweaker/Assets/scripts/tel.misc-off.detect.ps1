try {
    Get-GTTweakState -Registry @(
        @('HKCU:\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy', 'HasAccepted', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Personalization\Settings', 'AcceptedPrivacyPolicy', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\InputPersonalization', 'RestrictImplicitInkCollection', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\InputPersonalization', 'RestrictImplicitTextCollection', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\InputPersonalization', 'AllowInputPersonalization', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports', 'PreventHandwritingErrorReports', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\TabletPC', 'PreventHandwritingDataSharing', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore', 'HarvestContacts', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Input\TIPC', 'Enabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Microsoft\Input\TIPC', 'Enabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors', 'DisableSensors', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config', 'AutoConnectAllowedOEM', 'DWord', 0)
    )
} catch {
    'Unknown'
}
