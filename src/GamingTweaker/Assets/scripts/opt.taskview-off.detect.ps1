try {
    Get-GTTweakState -Registry @(,@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'ShowTaskViewButton', 'DWord', 0))
} catch {
    'Unknown'
}
