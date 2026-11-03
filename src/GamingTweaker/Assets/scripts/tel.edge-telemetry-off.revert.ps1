Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'DiagnosticData', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'MetricsReportingEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SendSiteInfoToImproveServices', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'UserFeedbackAllowed', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'SpotlightExperiencesAndRecommendationsEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'ShowRecommendationsEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'PromotionalTabsEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'PersonalizationReportingEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'MicrosoftEdgeInsiderPromotionEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Edge', 'ShowAcrobatSubscriptionButton', 'DWord', $null)
)
