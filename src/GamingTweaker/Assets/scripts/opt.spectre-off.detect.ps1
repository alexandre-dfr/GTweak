try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management', 'FeatureSettingsOverride', 'DWord', 3),
        @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management', 'FeatureSettingsOverrideMask', 'DWord', 3)
    )
} catch {
    'Unknown'
}
