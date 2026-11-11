try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Google\Chrome', 'ChromeCleanupReportingEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Google\Chrome', 'ChromeCleanupEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Google\Chrome', 'MetricsReportingEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Mozilla\Firefox', 'DisableDefaultBrowserAgent', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Mozilla\Firefox', 'DisableTelemetry', 'DWord', 1)
    ) `
        -Task @(
            '\Mozilla\Firefox Default Browser Agent 308046B0AF4A39CB',
            '\Mozilla\Firefox Default Browser Agent D2CEEC440E2074BD'
        )
} catch {
    'Unknown'
}
