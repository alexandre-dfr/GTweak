$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableActivityFeed' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'PublishUserActivities' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'UploadUserActivities' -Type DWord -Value 0
