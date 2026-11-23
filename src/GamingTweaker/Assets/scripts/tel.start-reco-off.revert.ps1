Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer', 'HideRecommendedSection', 'DWord', $null),
    @('HKCU:\Software\Policies\Microsoft\Windows\Explorer', 'HideRecommendedSection', 'DWord', $null),
    @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced', 'Start_IrisRecommendations', 'DWord', $null)
)
