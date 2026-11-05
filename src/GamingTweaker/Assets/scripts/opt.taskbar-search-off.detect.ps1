try {
    Get-GTTweakState -Registry @(,@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'SearchboxTaskbarMode', 'DWord', 0))
} catch {
    'Unknown'
}
