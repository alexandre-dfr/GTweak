try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'EdgeShoppingAssistantEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'ShowMicrosoftRewards', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'HubsSidebarEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'StandaloneHubsSidebarEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'NewTabPageBingChatEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'WebWidgetAllowed', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'WebWidgetIsEnabledOnStartup', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SearchbarAllowed', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SearchbarIsEnabledOnStartup', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'AddressBarMicrosoftSearchInBingProviderEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'EdgeCollectionsEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'HideFirstRunExperience', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'DefaultBrowserSettingsCampaignEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'WalletDonationEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'EdgeAssetDeliveryServiceEnabled', 'DWord', 0)
    )
} catch {
    'Unknown'
}
