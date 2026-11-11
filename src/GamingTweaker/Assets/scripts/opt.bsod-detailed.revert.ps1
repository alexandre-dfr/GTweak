Reset-GTTweaks -Registry @(
    @('HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl', 'DisplayParameters', 'DWord', $null),
    @('HKLM:\SYSTEM\CurrentControlSet\Control\CrashControl', 'DisableEmoticon', 'DWord', $null)
)
