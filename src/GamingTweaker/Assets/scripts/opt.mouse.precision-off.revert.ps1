$k = 'HKCU:\Control Panel\Mouse'
Set-TweakRegistry -Path $k -Name 'MouseSpeed' -Value '1' -Type String
Set-TweakRegistry -Path $k -Name 'MouseThreshold1' -Value '6' -Type String
Set-TweakRegistry -Path $k -Name 'MouseThreshold2' -Value '10' -Type String
