$k = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableAIDataAnalysis' -Type DWord -Value 1
