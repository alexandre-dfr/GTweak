try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings', 'FlightSettingsMaxPauseDays', 'DWord', 365))
} catch {
    'Unknown'
}
