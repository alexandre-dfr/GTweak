$session = New-Object -ComObject Microsoft.Update.Session
$found = $session.CreateUpdateSearcher().Search("IsInstalled=0 and Type='Software' and IsHidden=0 and BrowseOnly=0")
# Liste autorisée : les mises à jour logicielles trouvées (pilotes exclus par la requête), identifiées par leur numéro KB.
$byKb = [ordered]@{}
foreach ($u in $found.Updates) {
    if ($u.InstallationBehavior.CanRequestUserInput) { continue }
    if (@($u.Categories | Where-Object { $_.CategoryID -eq '3689bdc8-b205-4af4-8d4a-a63924c5e9d5' }).Count -gt 0) { continue }
    $kbs = @($u.KBArticleIDs)
    if ($kbs.Count -eq 0) { continue }
    $key = 'KB' + $kbs[0]
    if (-not $byKb.Contains($key)) { $byKb[$key] = $u }
}
# Un nom explicite qui n'est plus en attente (déjà installé) est ignoré, jamais exécuté.
$r = Resolve-GTListSelection -Selection $Selection -Allowed @($byKb.Keys)
if ($r.Names.Count -eq 0) { Skip-GTItem 'Aucune mise à jour à installer parmi la sélection (déjà installée, exclue ou absente).' }
$coll = New-Object -ComObject Microsoft.Update.UpdateColl
foreach ($n in $r.Names) {
    $u = $byKb[$n]
    if (-not $u.EulaAccepted) { $u.AcceptEula() }
    [void]$coll.Add($u)
}
$dl = $session.CreateUpdateDownloader()
$dl.Updates = $coll
$dr = $dl.Download()
if ($dr.ResultCode -ne 2 -and $dr.ResultCode -ne 3) { throw ('Téléchargement des mises à jour impossible (code ' + $dr.ResultCode + ').') }
$inst = $session.CreateUpdateInstaller()
$inst.Updates = $coll
$ir = $inst.Install()
$failed = @()
for ($i = 0; $i -lt $r.Names.Count; $i++) {
    $code = $ir.GetUpdateResult($i).ResultCode
    if ($code -ne 2) { $failed += ($r.Names[$i] + ' (code ' + $code + ')') }
}
if ($failed.Count -gt 0) { throw (($r.Names.Count - $failed.Count).ToString() + ' mise(s) à jour installée(s) ; échec pour : ' + ($failed -join ', ')) }
if ($ir.RebootRequired) { Set-GTItemNote ($r.Names.Count.ToString() + ' mise(s) à jour installée(s) ; un redémarrage est nécessaire pour les terminer.') }
else { Set-GTItemNote ($r.Names.Count.ToString() + ' mise(s) à jour installée(s).') }
