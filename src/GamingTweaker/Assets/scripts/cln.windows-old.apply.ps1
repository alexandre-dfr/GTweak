if (-not (Test-Path -LiteralPath ($env:SystemDrive + '\Windows.old'))) { Skip-GTItem 'Windows.old est absent de cette machine.' }
# Propriété et droits repris sur ce seul dossier (fichiers de TrustedInstaller) ; jonctions retirées sans être suivies.
Remove-TweakPath -Path ($env:SystemDrive + '\Windows.old') -RemoveRoot -RemoveLinks -TakeOwnership | Out-Null
