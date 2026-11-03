$k = 'HKCU:\Control Panel\Mouse'
Set-TweakRegistryIfNeeded -Path $k -Name 'MouseSpeed' -Type String -Value '0'
Set-TweakRegistryIfNeeded -Path $k -Name 'MouseThreshold1' -Type String -Value '0'
Set-TweakRegistryIfNeeded -Path $k -Name 'MouseThreshold2' -Type String -Value '0'
Update-GTMouseParams
