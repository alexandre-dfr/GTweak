try {
    Get-GTTweakState -Registry @(,@('HKCU:\Control Panel\Desktop', 'WindowArrangementActive', 'String', '0'))
} catch {
    'Unknown'
}
