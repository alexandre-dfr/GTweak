$allowed = @('Fax', 'RemoteRegistry', 'MapsBroker', 'PhoneSvc', 'RetailDemo', 'WpcMonSvc', 'lfsvc', 'CscService', 'SharedAccess')
$r = Resolve-GTListSelection -Selection $Selection -Allowed $allowed
if ($r.Unknown.Count -gt 0) { Skip-GTItem ('Services refusés, hors de la liste autorisée (rien n''a été modifié) : ' + ($r.Unknown -join ', ')) }
if ($r.Names.Count -eq 0) { Skip-GTItem 'Aucun service sélectionné.' }
$found = $false
foreach ($n in $r.Names) { if (Set-TweakServiceManualIfNeeded -Name $n) { $found = $true } }
if (-not $found) { Skip-GTItem 'Aucun des services choisis n''existe sur cette machine.' }
