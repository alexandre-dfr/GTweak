Remove-TweakPath -Path (Join-Path $env:SystemRoot 'Prefetch') -Filter '*.pf' -NoRecurse | Out-Null
