if (-not (Set-TweakServiceManualIfNeeded -Name 'WSearch')) { Skip-GTItem 'Le service WSearch est absent de cette machine.' }
Stop-Service -Name 'WSearch' -Force -ErrorAction SilentlyContinue
