# « Tout » est refusé : il désactiverait aussi les mises à jour des navigateurs et les tâches installées plus tard.
if (@($Selection) -contains '*') { Skip-GTItem '« Tout » n''est pas accepté pour les tâches planifiées : cochez les tâches une par une.' }
$tasks = @(Get-GTThirdPartyTasks)
$allowed = @($tasks | ForEach-Object { $_.Key })
$r = Resolve-GTListSelection -Selection $Selection -Allowed $allowed
if ($r.Unknown.Count -gt 0) { Skip-GTItem ('Tâches refusées, hors de la liste détectée (rien n''a été modifié) : ' + ($r.Unknown -join ', ')) }
if ($r.Names.Count -eq 0) { Skip-GTItem 'Aucune tâche planifiée sélectionnée.' }
foreach ($t in $tasks) { if ($r.Names -contains $t.Key) { [void](Set-TweakTaskIfNeeded -Path $t.Path -Enabled $false) } }
