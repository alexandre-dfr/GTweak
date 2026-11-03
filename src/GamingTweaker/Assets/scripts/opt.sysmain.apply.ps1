if (-not (Set-TweakServiceManualIfNeeded -Name 'SysMain')) { Skip-GTItem 'Le service SysMain est absent de cette machine.' }
Stop-Service -Name 'SysMain' -Force -ErrorAction SilentlyContinue
