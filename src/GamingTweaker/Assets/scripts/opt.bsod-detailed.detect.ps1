try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl', 'DisplayParameters', 'DWord', 1),
        @('HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl', 'DisableEmoticon', 'DWord', 1)
    )
} catch {
    'Unknown'
}
