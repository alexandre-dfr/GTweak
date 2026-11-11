Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'EdgeShoppingAssistantEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'ShowMicrosoftRewards', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'HubsSidebarEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'StandaloneHubsSidebarEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'NewTabPageBingChatEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'WebWidgetAllowed', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'WebWidgetIsEnabledOnStartup', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SearchbarAllowed', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SearchbarIsEnabledOnStartup', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'AddressBarMicrosoftSearchInBingProviderEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'EdgeCollectionsEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'HideFirstRunExperience', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'DefaultBrowserSettingsCampaignEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'WalletDonationEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'EdgeAssetDeliveryServiceEnabled', 'DWord', $null)
)
