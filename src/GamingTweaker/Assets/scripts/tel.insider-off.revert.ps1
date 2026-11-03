Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds', 'EnableExperimentation', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds', 'EnableConfigFlighting', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds', 'AllowBuildPreview', 'DWord', $null)
) `
    -Service @(,@('wisvc', 'Manual'))
