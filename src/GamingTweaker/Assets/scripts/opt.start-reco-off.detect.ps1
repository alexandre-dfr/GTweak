try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Start', 'HideRecommendedSection', 'DWord', 1),
        @('HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Education', 'IsEducationEnvironment', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer', 'HideRecommendedSection', 'DWord', 1)
    )
} catch {
    'Unknown'
}
