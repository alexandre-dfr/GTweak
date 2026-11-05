$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$cur = Get-GTRegValue $k 'SettingsPageVisibility'
$val = 'hide:home'
if ($cur.Existed -and -not [string]::IsNullOrWhiteSpace([string]$cur.Value)) {
    $txt = [string]$cur.Value
    if ($cur.Type -ne 'String') { Skip-GTItem 'La valeur SettingsPageVisibility existante n''est pas du texte : elle n''est pas modifiée.' }
    if ($txt -imatch '^showonly:') { Skip-GTItem 'Une stratégie existante n''affiche que certaines pages des Paramètres (showonly:) : elle n''est pas modifiée.' }
    if ($txt -imatch '^show:') {
        # « show:home » (laissé par un autre outil) rend la page visible : il est remplacé par « hide: » sans les autres pages.
        $val = 'hide:home'
    } elseif ($txt -imatch '^hide:') {
        $list = @($txt.Substring(5).Split(';') | Where-Object { $_ -ne '' })
        if ($list -contains 'home') { $val = $txt } else { $val = 'hide:' + (($list + 'home') -join ';') }
    } else { Skip-GTItem 'Format de SettingsPageVisibility inconnu : la valeur n''est pas modifiée.' }
}
Set-TweakRegistryIfNeeded -Path $k -Name 'SettingsPageVisibility' -Type String -Value $val
