try {
    Get-GTTweakState -Registry @(,@('HKCU:\Control Panel\Accessibility', 'DynamicScrollbars', 'DWord', 0))
} catch {
    'Unknown'
}
