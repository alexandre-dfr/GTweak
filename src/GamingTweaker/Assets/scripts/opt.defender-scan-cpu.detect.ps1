try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Scan', 'AvgCPULoadFactor', 'DWord', 5))
} catch {
    'Unknown'
}
