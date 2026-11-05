Remove-TweakPath -Path (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer') -Filter 'thumbcache_*.db' -NoRecurse | Out-Null
