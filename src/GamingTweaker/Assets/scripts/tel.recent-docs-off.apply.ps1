$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
Set-TweakRegistryIfNeeded -Path $k -Name 'NoRecentDocsHistory' -Type DWord -Value 1
$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'
Set-TweakRegistryIfNeeded -Path $k -Name 'ClearRecentDocsOnExit' -Type DWord -Value 1
$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer'
Set-TweakRegistryIfNeeded -Path $k -Name 'ShowRecent' -Type DWord -Value 0
