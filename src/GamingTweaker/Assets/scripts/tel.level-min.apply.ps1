$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowTelemetry' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowCommercialDataPipeline' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableOneSettingsDownloads' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DoNotShowFeedbackNotifications' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection'
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowTelemetry' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform'
Set-TweakRegistryIfNeeded -Path $k -Name 'NoGenTicket' -Type DWord -Value 1
$k = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy'
Set-TweakRegistryIfNeeded -Path $k -Name 'TailoredExperiencesWithDiagnosticDataEnabled' -Type DWord -Value 0
$k = 'HKCU:\SOFTWARE\Microsoft\Siuf\Rules'
Set-TweakRegistryIfNeeded -Path $k -Name 'NumberOfSIUFInPeriod' -Type DWord -Value 0
