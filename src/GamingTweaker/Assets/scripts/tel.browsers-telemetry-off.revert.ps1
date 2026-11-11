Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Google\Chrome', 'ChromeCleanupReportingEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Google\Chrome', 'ChromeCleanupEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Google\Chrome', 'MetricsReportingEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Mozilla\Firefox', 'DisableDefaultBrowserAgent', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Mozilla\Firefox', 'DisableTelemetry', 'DWord', $null)
) `
    -Task @(
        '\Mozilla\Firefox Default Browser Agent 308046B0AF4A39CB',
        '\Mozilla\Firefox Default Browser Agent D2CEEC440E2074BD'
    )
