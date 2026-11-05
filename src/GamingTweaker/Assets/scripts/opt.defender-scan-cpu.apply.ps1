$status = $null
try { $status = Get-MpComputerStatus } catch { }
if ($null -eq $status) { Skip-GTItem 'Microsoft Defender est indisponible ou remplacé par un autre antivirus.' }
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Scan' -Name 'AvgCPULoadFactor' -Type DWord -Value 5
$ok = $false
for ($i = 0; $i -lt 5; $i++) {
    if ([int](Get-MpPreference).ScanAvgCPULoadFactor -eq 5) { $ok = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ok) {
    $why = 'une gestion d''appareils ou une stratégie de groupe la remplace peut-être'
    if ($status.IsTamperProtected) { $why = 'la protection contre les falsifications de Sécurité Windows la bloque probablement' }
    throw ('La stratégie est écrite mais Defender ne l''applique pas : ' + $why + '. Aucun contournement n''est tenté ; vous pouvez annuler l''option pour retirer la stratégie.')
}
