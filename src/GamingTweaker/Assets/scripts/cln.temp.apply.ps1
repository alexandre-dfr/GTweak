# Dossier temporaire de l'utilisateur de la session (Get-GTUserTemp ; la variable TEMP du processus désigne le dossier Run protégé de l'outil) et celui de Windows.
$gtT = Get-GTUserTemp
if (-not $gtT) { Skip-GTItem 'Dossier temporaire de l''utilisateur introuvable.' }
Remove-TweakPath -Path @($gtT, (Join-Path $env:SystemRoot 'Temp')) | Out-Null
