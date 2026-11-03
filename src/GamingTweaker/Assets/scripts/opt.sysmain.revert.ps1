Reset-GTTweaks -Service @(,@('SysMain', 'Automatic'))
Start-Service -Name 'SysMain' -ErrorAction SilentlyContinue
