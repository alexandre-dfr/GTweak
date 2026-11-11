if (-not ((Test-Path -LiteralPath 'HKLM:\SOFTWARE\NVIDIA Corporation') -or (Test-Path -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm'))) {
    Skip-GTItem 'Aucun pilote NVIDIA détecté sur cette machine.'
}
$k = 'HKLM:\SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client'
Set-TweakRegistryIfNeeded -Path $k -Name 'OptInOrOutPreference' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\NVIDIA Corporation\Global\FTS'
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableRID44231' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableRID64640' -Type DWord -Value 0
Set-TweakRegistryIfNeeded -Path $k -Name 'EnableRID66610' -Type DWord -Value 0
$k = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\Startup'
Set-TweakRegistryIfNeeded -Path $k -Name 'SendTelemetryData' -Type DWord -Value 0
[void](Set-TweakServiceIfNeeded -Name 'NvTelemetryContainer' -StartType 'Manual')
[void](Set-TweakTaskIfNeeded -Path '\NvTmRep_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\NvTmRepOnLogon_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\NvTmMon_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}' -Enabled $false)
