try {
    $keys = @(Get-GTTcpInterfaceKeys)
    if ($keys.Count -eq 0) { 'NotApplied' }
    else {
        $reg = @()
        foreach ($k in $keys) {
            $reg += ,@($k, 'TcpAckFrequency', 'DWord', 1)
            $reg += ,@($k, 'TCPNoDelay', 'DWord', 1)
        }
        Get-GTTweakState -Registry $reg
    }
} catch {
    'Unknown'
}
