try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Driver Searching', 'SearchOrderConfig', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate', 'ExcludeWUDriversInQualityUpdate', 'DWord', 1)
    )
} catch {
    'Unknown'
}
