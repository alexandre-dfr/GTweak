$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat'
Set-TweakRegistryIfNeeded -Path $k -Name 'AITEnable' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableUAR' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableInventory' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisablePCA' -Type DWord -Value 1
[void](Set-TweakServiceIfNeeded -Name 'PcaSvc' -StartType 'Manual')
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\AitAgent' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\StartupAppTask' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\PcaPatchDbTask' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\SdbinstMergeDbTask' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\MareBackup' -Enabled $false)
