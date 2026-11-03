try {
    Get-GTTweakState -Registry @(
        @('HKCU:\Software\Policies\Microsoft\Windows\Explorer', 'DisableSearchBoxSuggestions', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer', 'DisableSearchBoxSuggestions', 'DWord', 1),
        @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search', 'BingSearchEnabled', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'DisableWebSearch', 'DWord', 1),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'ConnectedSearchUseWeb', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'ConnectedSearchUseWebOverMeteredConnections', 'DWord', 0),
        @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'EnableDynamicContentInWSB', 'DWord', 0)
    )
} catch {
    'Unknown'
}
