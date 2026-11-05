Reset-GTTweaks -Registry @(
    @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'AppsUseLightTheme', 'DWord', 1),
    @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'SystemUsesLightTheme', 'DWord', 1)
)
