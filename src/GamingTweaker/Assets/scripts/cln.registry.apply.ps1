# Uniquement : entrées Uninstall dont le programme a disparu (sans WindowsInstaller=1) et valeurs MuiCache dont le fichier a disparu.
# Chaque clé est exportée (.reg) dans le dossier de la session avant d'être supprimée ; si un export échoue, rien n'est supprimé.
Clear-TweakStaleRegistry
