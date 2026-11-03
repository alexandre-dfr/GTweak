$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
Set-TweakRegistryIfNeeded -Path $k -Name 'EdgeShoppingAssistantEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'ShowMicrosoftRewards' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'HubsSidebarEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'StandaloneHubsSidebarEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'NewTabPageBingChatEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'WebWidgetAllowed' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'WebWidgetIsEnabledOnStartup' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SearchbarAllowed' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SearchbarIsEnabledOnStartup' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'AddressBarMicrosoftSearchInBingProviderEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EdgeCollectionsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'HideFirstRunExperience' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DefaultBrowserSettingsCampaignEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'WalletDonationEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EdgeAssetDeliveryServiceEnabled' -Type DWord -Value 0
