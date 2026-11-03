try {
    Get-GTTweakState -Registry @(,@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects', 'VisualFXSetting', 'DWord', 2))
} catch {
    'Unknown'
}
