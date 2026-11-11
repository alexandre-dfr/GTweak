$gtOld = $env:SystemDrive + '\Windows.old'
if (-not (Test-Path -LiteralPath $gtOld)) { Skip-GTItem 'Windows.old est absent de cette machine.' }
# Propriété et droits repris sur ce seul dossier (fichiers de TrustedInstaller) ; jonctions retirées sans être suivies.
# Les dossiers AppData de chaque ancien profil (sauvegardes de jeux, réglages) sont exclus, comme les dossiers personnels.
$gtExcl = @(Get-ChildItem -LiteralPath (Join-Path $gtOld 'Users') -Directory -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'AppData' })
Remove-TweakPath -Path $gtOld -RemoveRoot -RemoveLinks -TakeOwnership -ExcludePath $gtExcl | Out-Null
