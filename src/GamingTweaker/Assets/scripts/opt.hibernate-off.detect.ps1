try {
    $v = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name 'HibernateEnabled' -ErrorAction Stop).HibernateEnabled
    if ([int64]$v -eq 0) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
