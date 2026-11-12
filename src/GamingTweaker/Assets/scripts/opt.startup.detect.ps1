# « Appliquée » si au moins un programme au démarrage existe et que tous sont désactivés ; sinon « Non appliquée ».
try {
    Get-GTStartupState
} catch {
    'Unknown'
}
