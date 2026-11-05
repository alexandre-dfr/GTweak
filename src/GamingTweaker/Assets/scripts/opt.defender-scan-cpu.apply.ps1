$status = $null
try { $status = Get-MpComputerStatus } catch { }
if ($null -eq $status) { Skip-GTItem 'Microsoft Defender est indisponible ou remplacé par un autre antivirus.' }
if ($status.IsTamperProtected) { throw 'La protection contre les falsifications de Sécurité Windows est active : Windows refuse cette stratégie. Ouvrez Sécurité Windows, Protection contre les virus et menaces, Gérer les paramètres, désactivez « Protection contre les falsifications », puis relancez cette option. Aucun contournement n''est appliqué.' }
Set-TweakRegistryIfNeeded -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Scan' -Name 'AvgCPULoadFactor' -Type DWord -Value 1
$ok = $false
for ($i = 0; $i -lt 5; $i++) {
    if ([int](Get-MpPreference).ScanAvgCPULoadFactor -eq 1) { $ok = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ok) { throw 'La stratégie est écrite mais Defender ne l''applique pas : une gestion d''appareils ou une stratégie de groupe la remplace. Rien de plus n''est tenté.' }
