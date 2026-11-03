$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search'
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowIndexingEncryptedStoresOrItems' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'AlwaysUseAutoLangDetection' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'PreventRemoteQueries' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path $k -Name 'AllowSearchToUseLocation' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'ConnectedSearchPrivacy' -Type DWord -Value 3
$k = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\SearchSettings'
Set-TweakRegistryIfNeeded -Path $k -Name 'IsMSACloudSearchEnabled' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'IsAADCloudSearchEnabled' -Type DWord -Value 0
