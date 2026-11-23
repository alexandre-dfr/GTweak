try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer', 'HideRecommendedSection', 'DWord', 1),
        @('HKCU:\Software\Policies\Microsoft\Windows\Explorer', 'HideRecommendedSection', 'DWord', 1),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'Start_IrisRecommendations', 'DWord', 0)
    )
} catch {
    'Unknown'
}
