Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowIndexingEncryptedStoresOrItems', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AlwaysUseAutoLangDetection', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'PreventRemoteQueries', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowSearchToUseLocation', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'ConnectedSearchPrivacy', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\SearchSettings', 'IsMSACloudSearchEnabled', 'DWord', 1),
    @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\SearchSettings', 'IsAADCloudSearchEnabled', 'DWord', 1)
)
