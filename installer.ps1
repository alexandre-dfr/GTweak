# Installe ou met à jour GamingTweaker : télécharge la dernière version publiée, vérifie son empreinte SHA-256,
# la place dans %LOCALAPPDATA%\Programs\GamingTweaker, ajoute un raccourci au menu Démarrer et la lance.
# Un fichier téléchargé ainsi ne porte pas la marque « téléchargé depuis Internet » : pas d'avertissement SmartScreen.
#
# Usage, dans PowerShell (pas besoin d'être administrateur ; l'appli demandera les droits à son lancement) :
#   irm https://raw.githubusercontent.com/alexandre-dfr/GTweak/main/installer.ps1 | iex
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'   # sinon Invoke-WebRequest est très lent sous Windows PowerShell 5.1
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $depot = 'alexandre-dfr/GTweak'
    $dossier = Join-Path $env:LOCALAPPDATA 'Programs\GamingTweaker'
    $exe = Join-Path $dossier 'GamingTweaker.exe'
    try {
        Write-Host 'Recherche de la dernière version de GamingTweaker...'
        $rel = Invoke-RestMethod "https://api.github.com/repos/$depot/releases/latest" -Headers @{ 'User-Agent' = 'GamingTweaker-installer' }
        $asset = @($rel.assets | Where-Object { $_.name -eq 'GamingTweaker.exe' }) | Select-Object -First 1
        if (-not $asset) { throw "la version $($rel.tag_name) ne contient pas GamingTweaker.exe." }
        if ($asset.browser_download_url -notlike "https://github.com/$depot/releases/download/*") { throw 'adresse de téléchargement inattendue.' }

        # Empreinte calculée par GitHub, sinon celle que la CI écrit dans les notes de la version.
        $attendu = $null
        if ([string]$asset.digest -match '^sha256:([0-9a-fA-F]{64})$') { $attendu = $Matches[1] }
        elseif ([string]$rel.body -match 'SHA-256 de GamingTweaker\.exe\s*:\s*`?([0-9A-Fa-f]{64})') { $attendu = $Matches[1] }
        if (-not $attendu) { throw 'empreinte SHA-256 introuvable : rien n''a été installé.' }

        New-Item -ItemType Directory -Force -Path $dossier | Out-Null
        $tmp = "$exe.nouveau"
        Write-Host "Téléchargement de GamingTweaker $($rel.tag_name)..."
        Invoke-WebRequest $asset.browser_download_url -OutFile $tmp -UseBasicParsing
        if ((Get-FileHash $tmp -Algorithm SHA256).Hash -ne $attendu) {
            Remove-Item $tmp -Force
            throw 'le fichier téléchargé ne correspond pas à l''empreinte publiée : rien n''a été installé.'
        }
        try { Move-Item $tmp $exe -Force }
        catch { Remove-Item $tmp -Force; throw 'GamingTweaker est ouvert : fermez-le (y compris l''icône près de l''horloge), puis relancez la commande.' }

        $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'GamingTweaker.lnk'
        $ws = New-Object -ComObject WScript.Shell
        $s = $ws.CreateShortcut($lnk); $s.TargetPath = $exe; $s.WorkingDirectory = $dossier; $s.Save()

        Write-Host "GamingTweaker $($rel.tag_name) est installé dans $dossier (raccourci dans le menu Démarrer)." -ForegroundColor Green
        Start-Process $exe
    } catch {
        Write-Host ("Installation impossible : " + $_.Exception.Message) -ForegroundColor Red
    }
}
