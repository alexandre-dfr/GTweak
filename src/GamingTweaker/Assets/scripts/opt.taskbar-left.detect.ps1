try {
    Get-GTTweakState -Registry @(,@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'TaskbarAl', 'DWord', 0))
} catch {
    'Unknown'
}
