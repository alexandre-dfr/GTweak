try {
    Get-GTTweakState -Registry @(,@('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager', 'DisableWpbtExecution', 'DWord', 1))
} catch {
    'Unknown'
}
