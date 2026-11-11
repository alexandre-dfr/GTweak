Reset-GTTweaks -Registry @(
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Driver Searching', 'SearchOrderConfig', 'DWord', 1),
    @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate', 'ExcludeWUDriversInQualityUpdate', 'DWord', $null)
)
