Reset-GTTweaks -Registry @(
    @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management', 'FeatureSettingsOverride', 'DWord', $null),
    @('HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management', 'FeatureSettingsOverrideMask', 'DWord', $null)
)
