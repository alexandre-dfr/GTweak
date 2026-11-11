try {
    Get-GTTweakState -Registry @(
        @('HKCU:\System\GameConfigStore', 'GameDVR_FSEBehaviorMode', 'DWord', 2),
        @('HKCU:\System\GameConfigStore', 'GameDVR_HonorUserFSEBehaviorMode', 'DWord', 1)
    )
} catch {
    'Unknown'
}
