try {
    Get-GTTweakState -Registry @(
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowIndexingEncryptedStoresOrItems', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AlwaysUseAutoLangDetection', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'PreventRemoteQueries', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'AllowSearchToUseLocation', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'ConnectedSearchPrivacy', 'DWord', 3),
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\SearchSettings', 'IsMSACloudSearchEnabled', 'DWord', 0),
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\SearchSettings', 'IsAADCloudSearchEnabled', 'DWord', 0)
    )
} catch {
    'Unknown'
}
