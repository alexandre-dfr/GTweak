try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'NoRecentDocsHistory', 'DWord', 1),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer', 'ClearRecentDocsOnExit', 'DWord', 1),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer', 'ShowRecent', 'DWord', 0)
    )
} catch {
    'Unknown'
}
