try {
    if ((Get-MpPreference).DisableRealtimeMonitoring) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
