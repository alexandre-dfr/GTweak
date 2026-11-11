Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Start', 'HideRecommendedSection', 'DWord', $null),
    @('HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Education', 'IsEducationEnvironment', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer', 'HideRecommendedSection', 'DWord', $null)
)
