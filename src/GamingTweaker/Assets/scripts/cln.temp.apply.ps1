# Le dossier de travail de GamingTweaker (scripts en cours d'exécution) est exclu.
Remove-TweakPath -Path @($env:TEMP, (Join-Path $env:SystemRoot 'Temp')) -ExcludePath (Join-Path $env:TEMP 'GamingTweaker') | Out-Null
