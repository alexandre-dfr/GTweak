try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Software\Microsoft\GameBar', 'AutoGameModeEnabled', 'DWord', 1),
        @('HKCU:\Software\Microsoft\GameBar', 'AllowAutoGameMode', 'DWord', 1)
    )
} catch {
    'Unknown'
}
