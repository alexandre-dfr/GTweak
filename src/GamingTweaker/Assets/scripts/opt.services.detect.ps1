try {
    Get-GTServiceManualState @('Fax', 'RemoteRegistry', 'MapsBroker', 'PhoneSvc', 'RetailDemo', 'WpcMonSvc', 'lfsvc', 'Spooler', 'CscService', 'StorSvc', 'SharedAccess')
} catch {
    'Unknown'
}
