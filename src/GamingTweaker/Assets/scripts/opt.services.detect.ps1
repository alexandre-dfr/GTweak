# « Appliquée » si tous les services de la liste présents sur la machine sont déjà hors démarrage automatique (quelle que soit la sélection) :
# il n'y a alors plus rien à faire. Sinon « Non appliquée » ; l'application reste idempotente.
try {
    Get-GTServiceManualState @('Fax', 'RemoteRegistry', 'MapsBroker', 'PhoneSvc', 'RetailDemo', 'WpcMonSvc', 'lfsvc', 'CscService', 'SharedAccess')
} catch {
    'Unknown'
}
