try {
    Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI', 'DisableAIDataAnalysis', 'DWord', 1))
} catch {
    'Unknown'
}
