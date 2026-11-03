try {
    Get-GTTweakState -Registry @(
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo', 'Enabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo', 'DisabledByGroupPolicy', 'DWord', 1)
    )
} catch {
    'Unknown'
}
