$k = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableSearchBoxSuggestions' -Type DWord -Value 1
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableSearchBoxSuggestions' -Type DWord -Value 1
$k = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search'
Set-TweakRegistryIfNeeded -Path $k -Name 'BingSearchEnabled' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableWebSearch' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'ConnectedSearchUseWeb' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'ConnectedSearchUseWebOverMeteredConnections' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableDynamicContentInWSB' -Type DWord -Value 0
