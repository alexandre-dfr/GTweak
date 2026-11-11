Reset-GTTweaks -Registry @(
    @('HKCU:\Software\Policies\Microsoft\Windows\Explorer', 'DisableSearchBoxSuggestions', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer', 'DisableSearchBoxSuggestions', 'DWord', $null),
    @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Search', 'BingSearchEnabled', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'DisableWebSearch', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'ConnectedSearchUseWeb', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'ConnectedSearchUseWebOverMeteredConnections', 'DWord', $null),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search', 'EnableDynamicContentInWSB', 'DWord', $null)
)
