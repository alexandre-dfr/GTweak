try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer', 'DisableCoInstallers', 'DWord', 1))
} catch {
    'Unknown'
}
