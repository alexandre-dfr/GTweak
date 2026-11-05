try {
    Get-GTTweakState -Registry @(
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'AppsUseLightTheme', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'SystemUsesLightTheme', 'DWord', 0)
    )
} catch {
    'Unknown'
}
