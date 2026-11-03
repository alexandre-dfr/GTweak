try {
    $s = Get-GTTweakState -Registry @(,@('HKLM:\SOFTWARE\Policies\WindowsNotepad', 'DisableAIFeatures', 'DWord', 1)) `
        -Service @(,@('WSAIFabricSvc', 'Disabled'))
    $v = Get-GTRegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'SettingsPageVisibility'
    $hidden = $v.Existed -and ([string]$v.Value -match 'aicomponents')
    $apps = @()
    foreach ($pkg in 'Microsoft.Copilot', 'Microsoft.Windows.Ai.Copilot.Provider') { $apps += @(Get-AppxPackage -Name $pkg -AllUsers -ErrorAction Stop) }
    if ($s -eq 'Applied' -and $hidden -and $apps.Count -eq 0) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
