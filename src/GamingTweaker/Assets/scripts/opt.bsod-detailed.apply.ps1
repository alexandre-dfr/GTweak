Set-TweakRegistryIfNeeded -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl' -Name 'DisplayParameters' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl' -Name 'DisableEmoticon' -Type DWord -Value 1
