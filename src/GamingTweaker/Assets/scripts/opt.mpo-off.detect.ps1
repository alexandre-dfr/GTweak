try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Microsoft\Windows\Dwm', 'OverlayTestMode', 'DWord', 5))
} catch {
    'Unknown'
}
