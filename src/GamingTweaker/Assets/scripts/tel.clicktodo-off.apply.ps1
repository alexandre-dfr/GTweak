Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' -Name 'DisableClickToDo' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path 'HKCU:\Software\Policies\Microsoft\Windows\WindowsAI' -Name 'DisableClickToDo' -Type DWord -Value 1
