Get-TweakPathSize -Path @($env:TEMP, (Join-Path $env:SystemRoot 'Temp')) -ExcludePath (Join-Path $env:TEMP 'GamingTweaker')
