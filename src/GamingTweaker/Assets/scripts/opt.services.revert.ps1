Reset-GTTweaks -Service @(
    @('Fax', 'Manual'),
    @('RemoteRegistry', 'Disabled'),
    @('MapsBroker', 'AutomaticDelayed'),
    @('PhoneSvc', 'Manual'),
    @('RetailDemo', 'Manual'),
    @('WpcMonSvc', 'Manual'),
    @('lfsvc', 'Manual'),
    @('CscService', 'Manual'),
    @('SharedAccess', 'Manual')
)
