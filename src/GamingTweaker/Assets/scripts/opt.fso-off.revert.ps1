Reset-GTTweaks -Registry @(
    @('HKCU:\System\GameConfigStore', 'GameDVR_FSEBehaviorMode', 'DWord', $null),
    @('HKCU:\System\GameConfigStore', 'GameDVR_HonorUserFSEBehaviorMode', 'DWord', $null)
)
