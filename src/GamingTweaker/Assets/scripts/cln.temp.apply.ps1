# Dossier temporaire de l'utilisateur du bureau (jamais $env:TEMP, qui est le dossier Run protégé de l'outil) et celui de Windows.
$gtT = Get-GTUserTemp
if (-not $gtT) { Skip-GTItem 'Dossier temporaire de l''utilisateur introuvable.' }
Remove-TweakPath -Path @($gtT, (Join-Path $env:SystemRoot 'Temp')) | Out-Null
