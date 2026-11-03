$k = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting'
Set-TweakRegistryIfNeeded -Path $k -Name 'Disabled' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DontSendAdditionalData' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'LoggingDisabled' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting'
Set-TweakRegistryIfNeeded -Path $k -Name 'Disabled' -Type DWord -Value 1
[void](Set-TweakServiceIfNeeded -Name 'WerSvc' -StartType 'Disabled')
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Windows Error Reporting\QueueReporting' -Enabled $false)
