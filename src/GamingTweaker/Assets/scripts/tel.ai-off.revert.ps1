Reset-GTTweaks -Registry @(,@('HKLM:\SOFTWARE\Policies\WindowsNotepad', 'DisableAIFeatures', 'DWord', $null)) `
    -Service @(,@('WSAIFabricSvc', 'Automatic'))
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
$cur = Get-GTRegValue $k 'SettingsPageVisibility'
if ($cur.Existed -and ([string]$cur.Value) -match 'aicomponents') {
    $rest = @(([string]$cur.Value).Substring(5) -split ';' | Where-Object { $_ -and $_ -ne 'aicomponents' })
    if ($rest.Count -eq 0) { Remove-ItemProperty -LiteralPath $k -Name 'SettingsPageVisibility' -ErrorAction Stop }
    else { Set-GTRegRaw $k 'SettingsPageVisibility' 'String' ('hide:' + ($rest -join ';')) }
}
