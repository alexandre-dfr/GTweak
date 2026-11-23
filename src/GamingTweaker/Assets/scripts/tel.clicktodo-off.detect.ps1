try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI', 'DisableClickToDo', 'DWord', 1),
        @('HKCU:\Software\Policies\Microsoft\Windows\WindowsAI', 'DisableClickToDo', 'DWord', 1)
    )
} catch {
    'Unknown'
}
