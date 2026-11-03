$k = 'HKCU:\Control Panel\Mouse'
Set-TweakRegistry -Path $k -Name 'MouseSpeed' -Value '0' -Type String
Set-TweakRegistry -Path $k -Name 'MouseThreshold1' -Value '0' -Type String
Set-TweakRegistry -Path $k -Name 'MouseThreshold2' -Value '0' -Type String
