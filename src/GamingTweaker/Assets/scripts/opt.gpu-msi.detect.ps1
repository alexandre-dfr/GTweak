try {
    $keys = @(Get-GTGpuMsiKeys)
    if ($keys.Count -eq 0) { 'NotApplied' }
    else {
        $reg = @()
        foreach ($k in $keys) { $reg += ,@($k, 'MSISupported', 'DWord', 1) }
        Get-GTTweakState -Registry $reg
    }
} catch {
    'Unknown'
}
