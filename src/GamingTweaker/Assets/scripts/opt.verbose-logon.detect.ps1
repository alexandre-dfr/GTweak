try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System', 'VerboseStatus', 'DWord', 1))
} catch {
    'Unknown'
}
