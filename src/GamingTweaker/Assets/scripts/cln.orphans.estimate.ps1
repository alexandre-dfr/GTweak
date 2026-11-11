# lent : lecture de tous les produits Windows Installer.
$gtTotal = [long]0
foreach ($gtF in @(Find-OrphanInstallerFiles)) { $gtTotal += (Get-Item -LiteralPath $gtF).Length }
$gtTotal
