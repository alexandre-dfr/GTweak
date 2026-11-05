try {
    Get-GTTweakState -Registry @(,@('HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem', 'LongPathsEnabled', 'DWord', 1))
} catch {
    'Unknown'
}
