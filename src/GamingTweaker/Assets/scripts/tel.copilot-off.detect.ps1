try {
    Get-GTTweakState -Registry @(,@('HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot', 'TurnOffWindowsCopilot', 'DWord', 1))
} catch {
    'Unknown'
}
