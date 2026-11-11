# Liste autorisée (nom de paquet = identifiant Microsoft Store, vide si inconnu) : même contenu que bloatware-list.json (test de cohérence).
$known = [ordered]@{
    'SpotifyAB.SpotifyMusic' = '9NCBCSZSJRSB'
    'king.com.CandyCrushSaga' = ''
    'king.com.CandyCrushSodaSaga' = ''
    'king.com.CandyCrushFriends' = ''
    'Duolingo-LearnLanguagesforFree' = ''
    'Flipboard.Flipboard' = ''
    'ShazamEntertainmentLtd.Shazam' = ''
    'A278AB0D.MarchofEmpires' = ''
    'Twitter' = ''
    'iHeartRadio' = ''
    'PandoraMediaInc.29680B314EFC2' = ''
    'AdobeSystemsIncorporated.AdobePhotoshopExpress' = ''
    'EclipseManager' = ''
    'ActiproSoftwareLLC.562882FEEB491' = ''
}
if (@($Selection | Where-Object { $_ -eq '*' -or $_.StartsWith('!') }).Count -gt 0) { Skip-GTItem 'Choisissez les applications une à une : « Tout » n''est pas accepté pour ce menu (rien n''a été modifié).' }
$r = Resolve-GTListSelection -Selection $Selection -Allowed @($known.Keys)
if ($r.Unknown.Count -gt 0) { Skip-GTItem ('Applications refusées, hors de la liste autorisée (rien n''a été modifié) : ' + ($r.Unknown -join ', ')) }
if ($r.Names.Count -eq 0) { Skip-GTItem 'Aucune application sélectionnée.' }
$done = 0; $errors = @()
foreach ($n in $r.Names) {
    try {
        if ($n -eq 'OneDrive') { Remove-TweakOneDrive } else { Remove-TweakApp -Package $n -StoreId $known[$n] }
        $done++
    } catch {
        $m = [string]$_.Exception.Message
        if (-not $m.StartsWith('GTSKIP:')) { $errors += ($n + ' : ' + $m) }
    }
}
if ($errors.Count -gt 0) { throw ('Échec pour : ' + ($errors -join ' ; ')) }
if ($done -eq 0) { Skip-GTItem 'Aucune des applications choisies n''est présente sur cette machine.' }
Set-GTItemNote ($done.ToString() + ' application(s) tierce(s) retirée(s). Réinstallation possible depuis le Microsoft Store (annulation).')
