$k = 'HKLM:\SOFTWARE\Policies\BraveSoftware\Brave'
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveRewardsDisabled' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveWalletDisabled' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveVPNDisabled' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveAIChatEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveStatsPingEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveNewsDisabled' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveTalkDisabled' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'BraveP3AEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'UrlKeyedAnonymizedDataCollectionEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SafeBrowsingExtendedReportingEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'MetricsReportingEnabled' -Type DWord -Value 0
