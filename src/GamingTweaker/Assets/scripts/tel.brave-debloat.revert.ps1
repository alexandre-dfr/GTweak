Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveRewardsDisabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveWalletDisabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveVPNDisabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveAIChatEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveStatsPingEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveNewsDisabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveTalkDisabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveP3AEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'UrlKeyedAnonymizedDataCollectionEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'SafeBrowsingExtendedReportingEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'MetricsReportingEnabled', 'DWord', $null)
)
