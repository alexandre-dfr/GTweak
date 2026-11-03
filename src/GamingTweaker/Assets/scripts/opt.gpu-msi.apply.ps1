$keys = @(Get-GTGpuMsiKeys)
if ($keys.Count -eq 0) { Skip-GTItem 'Aucune carte graphique PCI détectée.' }
foreach ($k in $keys) { Set-TweakRegistryIfNeeded -Path $k -Name 'MSISupported' -Type DWord -Value 1 }
