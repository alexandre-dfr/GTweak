$k = 'HKLM:\SOFTWARE\Policies\WindowsNotepad'
Set-TweakRegistryIfNeeded -Path $k -Name 'DisableAIFeatures' -Type DWord -Value 1
[void](Set-TweakServiceIfNeeded -Name 'WSAIFabricSvc' -StartType 'Disabled')
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$cur = Get-GTRegValue $k 'SettingsPageVisibility'
if (-not $cur.Existed) {
    Set-TweakRegistry -Path $k -Name 'SettingsPageVisibility' -Type String -Value 'hide:aicomponents'
} elseif ($cur.Type -eq 'String' -and ([string]$cur.Value).StartsWith('hide:') -and ([string]$cur.Value) -notmatch 'aicomponents') {
    Set-TweakRegistry -Path $k -Name 'SettingsPageVisibility' -Type String -Value (([string]$cur.Value).TrimEnd(';') + ';aicomponents')
}
foreach ($pkg in 'Microsoft.Copilot', 'Microsoft.Windows.Ai.Copilot.Provider') {
    if (@(Get-AppxPackage -Name $pkg -AllUsers -ErrorAction SilentlyContinue).Count -gt 0) { Remove-TweakApp -Package $pkg }
}
