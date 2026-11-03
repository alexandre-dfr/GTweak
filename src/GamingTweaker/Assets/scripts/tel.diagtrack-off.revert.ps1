Reset-GTTweaks -Service @(
    @('DiagTrack', 'Automatic'),
    @('dmwappushservice', 'Manual')
)
Start-Service -Name 'DiagTrack' -ErrorAction SilentlyContinue
