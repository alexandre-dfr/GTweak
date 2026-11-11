$reg = @()
foreach ($k in @(Get-GTTcpInterfaceKeys)) {
    $reg += ,@($k, 'TcpAckFrequency', 'DWord', $null)
    $reg += ,@($k, 'TCPNoDelay', 'DWord', $null)
}
Reset-GTTweaks -Registry $reg
