# Stratégie Explorer seulement (jamais IsEducationEnvironment, voir docs/revue-code-2026-11-11.md) ; Start_IrisRecommendations coupe les astuces et nouvelles applis.
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' -Name 'HideRecommendedSection' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' -Name 'HideRecommendedSection' -Type DWord -Value 1
Set-TweakRegistryIfNeeded -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'Start_IrisRecommendations' -Type DWord -Value 0
