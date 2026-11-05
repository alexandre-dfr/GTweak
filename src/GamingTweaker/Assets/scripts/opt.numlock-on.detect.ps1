try {
    Get-GTTweakState -Registry @(,@('HKCU:\Control Panel\Keyboard', 'InitialKeyboardIndicators', 'String', '2'))
} catch {
    'Unknown'
}
