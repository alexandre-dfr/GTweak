try {
    if (Get-GTHibernateEnabled) { 'NotApplied' } else { 'Applied' }
} catch {
    'Unknown'
}
