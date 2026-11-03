$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
Set-TweakRegistryIfNeeded -Path $k -Name 'DiagnosticData' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'MetricsReportingEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SendSiteInfoToImproveServices' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'UserFeedbackAllowed' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'SpotlightExperiencesAndRecommendationsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'ShowRecommendationsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'PromotionalTabsEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'PersonalizationReportingEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'MicrosoftEdgeInsiderPromotionEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'ShowAcrobatSubscriptionButton' -Type DWord -Value 0
