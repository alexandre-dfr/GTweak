try {
    $cur = Get-GTRegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'SettingsPageVisibility'
    if ($cur.Existed -and $cur.Type -eq 'String' -and ([string]$cur.Value -imatch '^hide:(.*;)?home(;.*)?$')) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
