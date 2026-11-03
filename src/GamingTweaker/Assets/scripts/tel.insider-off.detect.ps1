try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds', 'EnableExperimentation', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds', 'EnableConfigFlighting', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds', 'AllowBuildPreview', 'DWord', 0)
    ) `
        -Service @(,@('wisvc', 'Disabled'))
} catch {
    'Unknown'
}
