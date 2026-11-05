$v = 2
if ((Get-GTWindowsBuild) -ge 22000) { $v = 1 }
Reset-GTTweaks -Registry @(,@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Search', 'SearchboxTaskbarMode', 'DWord', $v))
