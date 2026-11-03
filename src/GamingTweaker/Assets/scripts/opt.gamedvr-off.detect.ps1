try {
    Get-GTTweakState -Registry @(,@('HKCU:\System\GameConfigStore', 'GameDVR_Enabled', 'DWord', 0))
} catch {
    'Unknown'
}
