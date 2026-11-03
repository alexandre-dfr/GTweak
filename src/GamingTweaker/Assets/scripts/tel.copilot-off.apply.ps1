$k = 'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot'
Set-TweakRegistryIfNeeded -Path $k -Name 'TurnOffWindowsCopilot' -Type DWord -Value 1
