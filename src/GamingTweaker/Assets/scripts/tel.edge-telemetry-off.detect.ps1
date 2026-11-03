try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'DiagnosticData', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'MetricsReportingEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SendSiteInfoToImproveServices', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'UserFeedbackAllowed', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SpotlightExperiencesAndRecommendationsEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'ShowRecommendationsEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'PromotionalTabsEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'PersonalizationReportingEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'MicrosoftEdgeInsiderPromotionEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'ShowAcrobatSubscriptionButton', 'DWord', 0)
    )
} catch {
    'Unknown'
}
