$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\SettingSync'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableSettingSync' -Type DWord -Value 2
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableSettingSyncUserOverride' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableSyncOnPaidNetwork' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'SyncPolicy' -Type DWord -Value 5
