$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowOnlineTips' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
Set-TweakRegistryIfNeeded -Path $k -Name 'NoInternetOpenWith' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'NoOnlinePrintsWizard' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'NoPublishingWizard' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'NoWebServices' -Type DWord -Value 1
