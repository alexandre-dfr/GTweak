try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile', 'NetworkThrottlingIndex', 'DWord', 4294967295))
} catch {
    'Unknown'
}
