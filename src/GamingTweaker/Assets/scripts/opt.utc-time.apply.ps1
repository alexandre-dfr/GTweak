$k = 'HKLM:\SYSTEM\CurrentControlSet\Control\TimeZoneInformation'
$cur = Get-GTRegValue $k 'RealTimeIsUniversal'
if (-not ($cur.Existed -and (($cur.Type -eq 'QWord') -or ($cur.Type -eq 'DWord')) -and ([int64]$cur.Value -eq 1))) {
    Set-TweakRegistry -Path $k -Name 'RealTimeIsUniversal' -Type QWord -Value 1
}
