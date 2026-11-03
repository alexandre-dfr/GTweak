Reset-GTTweaks -Registry @(
    @('HKCU:\Control Panel\Accessibility\StickyKeys', 'Flags', 'String', '510'),
    @('HKCU:\Control Panel\Accessibility\Keyboard Response', 'Flags', 'String', '126'),
    @('HKCU:\Control Panel\Accessibility\ToggleKeys', 'Flags', 'String', '62')
)
