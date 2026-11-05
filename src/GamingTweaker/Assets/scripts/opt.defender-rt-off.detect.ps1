try {
    if ((Get-MpComputerStatus).RealTimeProtectionEnabled) { 'NotApplied' } else { 'Applied' }
} catch {
    'Unknown'
}
