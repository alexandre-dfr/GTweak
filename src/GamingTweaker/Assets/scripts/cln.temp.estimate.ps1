$gtT = Get-GTUserTemp
if (-not $gtT) { 0 } else { Get-TweakPathSize -Path @($gtT, (Join-Path $env:SystemRoot 'Temp')) }
