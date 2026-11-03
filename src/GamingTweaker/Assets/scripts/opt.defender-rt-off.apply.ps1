$status = $null
try { $status = Get-MpComputerStatus } catch { }
if ($null -eq $status) { Skip-GTItem 'Microsoft Defender est indisponible ou remplacé par un autre antivirus.' }
if ($status.IsTamperProtected) { throw 'La protection contre les falsifications de Sécurité Windows est active : Windows refuse de couper la protection en temps réel. Ouvrez Sécurité Windows, Protection contre les virus et menaces, Gérer les paramètres, désactivez « Protection contre les falsifications », puis relancez cette option. Aucun contournement n''est appliqué.' }
if (-not (Get-MpPreference).DisableRealtimeMonitoring) { Set-TweakDefenderRealtime -Enabled $false }
