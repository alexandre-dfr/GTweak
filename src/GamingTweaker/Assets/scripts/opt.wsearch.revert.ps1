Reset-GTTweaks -Service @(,@('WSearch', 'AutomaticDelayed'))
Start-Service -Name 'WSearch' -ErrorAction SilentlyContinue
