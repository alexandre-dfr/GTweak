Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI', 'DisableClickToDo', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\Windows\WindowsAI', 'DisableClickToDo', 'DWord', $null)
)
