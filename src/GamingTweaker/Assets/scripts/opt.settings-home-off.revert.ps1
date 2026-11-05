$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$cur = Get-GTRegValue $k 'SettingsPageVisibility'
if ($cur.Existed -and $cur.Type -eq 'String' -and ([string]$cur.Value -imatch '^hide:')) {
    $list = @(([string]$cur.Value).Substring(5).Split(';') | Where-Object { ($_ -ne '') -and ($_ -ine 'home') })
    if ($list.Count -eq 0) { Reset-GTTweaks -Registry @(,@($k, 'SettingsPageVisibility', 'String', $null)) }
    else { Set-GTRegRaw $k 'SettingsPageVisibility' 'String' ('hide:' + ($list -join ';')) }
}
