Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoRecentDocsHistory', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'ClearRecentDocsOnExit', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer', 'ShowRecent', 'DWord', 1)
)
