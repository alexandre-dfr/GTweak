$r = Resolve-GTListSelection -Selection $Selection -Allowed @('GameReady', 'Studio')
if ($r.Unknown.Count -gt 0) { Skip-GTItem ('Branches refusées, hors de la liste autorisée (rien n''a été modifié) : ' + ($r.Unknown -join ', ')) }
if ($r.Names.Count -ne 1) { Skip-GTItem 'Choisissez une seule branche de pilote : Game Ready ou Studio.' }
$smi = Get-GTNvidiaSmi
if (-not $smi) { Skip-GTItem 'Aucun GPU NVIDIA détecté (nvidia-smi introuvable).' }
$q = Invoke-GTNative $smi @('--query-gpu=name,driver_version', '--format=csv,noheader')
if ($q.ExitCode -ne 0 -or $q.Output.Count -eq 0) { Skip-GTItem 'Aucun GPU NVIDIA détecté.' }
$parts = ([string]$q.Output[0]).Split(',')
if ($parts.Count -lt 2) { throw 'Réponse inattendue de nvidia-smi.' }
$gpu = $parts[0].Trim(); $installed = $parts[1].Trim()
$latest = Get-GTNvidiaLatest -GpuName $gpu -Branch $r.Names[0]
if (-not $latest) { throw 'La recherche de la dernière version a échoué. Téléchargez le pilote depuis la page officielle : https://www.nvidia.com/fr-fr/drivers/' }
if ((Compare-GTDriverVersion $installed $latest.Version) -ge 0) { Skip-GTItem ('Pilote déjà à jour (version ' + $installed + ').') }
Install-TweakNvidiaDriver -Url $latest.Url | Out-Null
Set-GTItemNote ('Pilote NVIDIA ' + $latest.Version + ' installé (ancienne version : ' + $installed + '). Un redémarrage est conseillé.')
