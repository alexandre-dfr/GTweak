try {
    Get-GTTweakState -Registry @(,@('HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers', 'HwSchMode', 'DWord', 2))
} catch {
    'Unknown'
}
