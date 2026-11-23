# « Appliquée » si au moins une tâche planifiée d'application existe et que toutes sont désactivées ; sinon « Non appliquée ».
try {
    Get-GTThirdPartyTasksState
} catch {
    'Unknown'
}
