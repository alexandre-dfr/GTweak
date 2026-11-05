$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$cur = Get-GTRegValue $k 'SettingsPageVisibility'
$val = 'hide:home'
if ($cur.Existed -and -not [string]::IsNullOrWhiteSpace([string]$cur.Value)) {
    $txt = [string]$cur.Value
    if ($cur.Type -ne 'String' -or $txt -inotmatch '^hide:') { Skip-GTItem 'Une stratégie existante limite déjà les pages des Paramètres (showonly:) : elle n''est pas modifiée.' }
    $list = @($txt.Substring(5).Split(';') | Where-Object { $_ -ne '' })
    if ($list -contains 'home') { $val = $txt } else { $val = 'hide:' + (($list + 'home') -join ';') }
}
Set-TweakRegistryIfNeeded -Path $k -Name 'SettingsPageVisibility' -Type String -Value $val
