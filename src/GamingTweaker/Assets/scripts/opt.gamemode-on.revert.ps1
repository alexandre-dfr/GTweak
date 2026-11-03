Reset-GTTweaks -Registry @(
    @('HKCU:\Software\Microsoft\GameBar', 'AutoGameModeEnabled', 'DWord', $null),
    @('HKCU:\Software\Microsoft\GameBar', 'AllowAutoGameMode', 'DWord', $null)
)
