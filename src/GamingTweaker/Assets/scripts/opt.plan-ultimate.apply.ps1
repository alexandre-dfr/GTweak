# « Performances ultimes » est un modèle caché de Windows : il ne s'active pas tel quel. Une copie est créée une seule fois sous un
# identifiant propre à GamingTweaker, puis activée. Un plan déjà actif sous l'identifiant du modèle (ancienne version) est accepté.
$modele = 'e9a42b02-d5df-448d-aa00-03f14749eb61'
$copie = 'd8f3a1c2-5e7b-4c9a-9f10-67616d696e67'
$actif = Get-GTActiveScheme
if ($actif -ieq $modele -or $actif -ieq $copie) { return }
Initialize-GTScheme -Guid $copie -Source $modele
Set-TweakActiveScheme -Guid $copie
