try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization', 'NoLockScreen', 'DWord', 1))
} catch {
    'Unknown'
}
