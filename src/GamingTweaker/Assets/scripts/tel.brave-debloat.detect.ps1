try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveRewardsDisabled', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveWalletDisabled', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveVPNDisabled', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveAIChatEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveStatsPingEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveNewsDisabled', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveTalkDisabled', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'BraveP3AEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'UrlKeyedAnonymizedDataCollectionEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'SafeBrowsingExtendedReportingEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\BraveSoftware\Brave', 'MetricsReportingEnabled', 'DWord', 0)
    )
} catch {
    'Unknown'
}
