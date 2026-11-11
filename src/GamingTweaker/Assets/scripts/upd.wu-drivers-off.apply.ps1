Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Driver Searching' -Name 'SearchOrderConfig' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -Name 'ExcludeWUDriversInQualityUpdate' -Type DWord -Value 1
