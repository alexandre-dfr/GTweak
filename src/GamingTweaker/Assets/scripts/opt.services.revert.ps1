Reset-GTTweaks -Service @(
    @('Fax', 'Manual'),
    @('RemoteRegistry', 'Disabled'),
    @('MapsBroker', 'AutomaticDelayed'),
    @('PhoneSvc', 'Manual'),
    @('RetailDemo', 'Manual'),
    @('WpcMonSvc', 'Manual'),
    @('lfsvc', 'Manual'),
    @('Spooler', 'Automatic'),
    @('CscService', 'Manual'),
    @('StorSvc', 'AutomaticDelayed'),
    @('SharedAccess', 'Manual')
)
