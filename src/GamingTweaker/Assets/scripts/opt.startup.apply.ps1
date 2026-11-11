$entries = @(Get-GTStartupEntries)
$allowed = @($entries | ForEach-Object { $_.Key })
$r = Resolve-GTListSelection -Selection $Selection -Allowed $allowed
if ($r.Unknown.Count -gt 0) { Skip-GTItem ('Entrées de démarrage refusées, hors de la liste détectée (rien n''a été modifié) : ' + ($r.Unknown -join ', ')) }
if ($r.Names.Count -eq 0) { Skip-GTItem 'Aucun programme de démarrage sélectionné.' }
foreach ($e in $entries) { if ($r.Names -contains $e.Key) { Set-TweakStartupDisabled -Entry $e } }
