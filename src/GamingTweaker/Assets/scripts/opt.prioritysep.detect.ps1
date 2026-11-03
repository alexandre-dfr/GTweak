try {
    Get-GTTweakState -Registry @(,@('HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl', 'Win32PrioritySeparation', 'DWord', 0x26))
} catch {
    'Unknown'
}
