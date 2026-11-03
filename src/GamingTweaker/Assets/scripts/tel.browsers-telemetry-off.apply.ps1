$k = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
Set-TweakRegistryIfNeeded -Path $k -Name 'ChromeCleanupReportingEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'ChromeCleanupEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'MetricsReportingEnabled' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableDefaultBrowserAgent' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableTelemetry' -Type DWord -Value 1
[void](Set-TweakTaskIfNeeded -Path '\Mozilla\Firefox Default Browser Agent 308046B0AF4A39CB' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Mozilla\Firefox Default Browser Agent D2CEEC440E2074BD' -Enabled $false)
