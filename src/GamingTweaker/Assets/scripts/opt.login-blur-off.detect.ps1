try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System', 'DisableAcrylicBackgroundOnLogon', 'DWord', 1))
} catch {
    'Unknown'
}
