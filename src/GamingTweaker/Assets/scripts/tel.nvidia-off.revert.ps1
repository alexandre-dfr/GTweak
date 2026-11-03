Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client', 'OptInOrOutPreference', 'DWord', 1),
    @('HKLM:\SOFTWARE\NVIDIA Corporation\Global\FTS', 'EnableRID44231', 'DWord', 1),
    @('HKLM:\SOFTWARE\NVIDIA Corporation\Global\FTS', 'EnableRID64640', 'DWord', 1),
    @('HKLM:\SOFTWARE\NVIDIA Corporation\Global\FTS', 'EnableRID66610', 'DWord', 1),
    @('HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\Startup', 'SendTelemetryData', 'DWord', $null)
) `
    -Service @(,@('NvTelemetryContainer', 'Automatic')) `
    -Task @(
        '\NvTmRep_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}',
        '\NvTmRepOnLogon_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}',
        '\NvTmMon_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}'
    )
