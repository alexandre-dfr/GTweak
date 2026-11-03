$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds'
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableExperimentation' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableConfigFlighting' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowBuildPreview' -Type DWord -Value 0
[void](Set-TweakServiceIfNeeded -Name 'wisvc' -StartType 'Disabled')
