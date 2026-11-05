try {
    Get-GTTweakState -Registry @(,@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings', 'TaskbarEndTask', 'DWord', 1))
} catch {
    'Unknown'
}
