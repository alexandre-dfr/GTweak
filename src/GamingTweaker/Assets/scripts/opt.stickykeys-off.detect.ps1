try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Control Panel\Accessibility\StickyKeys', 'Flags', 'String', '506'),
        @('HKCU:\Control Panel\Accessibility\Keyboard Response', 'Flags', 'String', '122'),
        @('HKCU:\Control Panel\Accessibility\ToggleKeys', 'Flags', 'String', '58')
    )
} catch {
    'Unknown'
}
