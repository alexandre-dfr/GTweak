Reset-GTTweaks -Registry @(
    @('HKCU:\Control Panel\Mouse', 'MouseSpeed', 'String', '1'),
    @('HKCU:\Control Panel\Mouse', 'MouseThreshold1', 'String', '6'),
    @('HKCU:\Control Panel\Mouse', 'MouseThreshold2', 'String', '10')
)
Update-GTMouseParams
