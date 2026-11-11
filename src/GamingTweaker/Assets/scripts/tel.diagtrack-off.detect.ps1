try {
    Get-GTTweakState -Service @(
        @('DiagTrack', 'Disabled'),
        @('dmwappushservice', 'Disabled')
    )
} catch {
    'Unknown'
}
