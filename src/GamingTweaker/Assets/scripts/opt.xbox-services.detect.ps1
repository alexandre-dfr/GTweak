# « Appliquée » si tous les services Xbox présents sont déjà hors démarrage automatique (quelle que soit la sélection) ; sinon « Non appliquée ».
try {
    Get-GTServiceManualState @('XblAuthManager', 'XblGameSave', 'XboxGipSvc', 'XboxNetApiSvc')
} catch {
    'Unknown'
}
