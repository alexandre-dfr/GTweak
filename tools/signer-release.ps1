# Signature des versions de GamingTweaker (cle RSA 3072 bits du mainteneur). A lancer sur le PC du mainteneur, jamais dans la CI.
# La mise a jour integree de l'appli n'installe un exe que si sa signature correspond a une cle de src/GamingTweaker/Assets/cles-release.txt.
#
#   -Generer            cree la cle (une seule fois), ajoute la cle publique a cles-release.txt (a commiter)
#   -Exporter <fichier> ecrit la cle privee en clair pour une sauvegarde hors ligne (cle USB rangee a part)
#   -Importer <fichier> reprend une cle privee sauvegardee (nouveau PC)
#   -Tag vX.Y.Z         telecharge l'exe publie, le controle, demande confirmation, signe et joint GamingTweaker.exe.sig a la release
#
# La cle privee est stockee chiffree pour le compte Windows courant (DPAPI) dans %APPDATA%\GamingTweaker-release\cle-privee.bin.
# Usage : powershell -ExecutionPolicy Bypass -File tools\signer-release.ps1 -Tag v0.9.3
[CmdletBinding()]
param(
    [switch]$Generer,
    [string]$Exporter,
    [string]$Importer,
    [string]$Tag,
    [string]$CleFichier = (Join-Path $env:APPDATA 'GamingTweaker-release\cle-privee.bin')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Security

$depot = 'alexandre-dfr/GTweak'
$racine = Split-Path -Parent $PSScriptRoot
$fichierCles = Join-Path $racine 'src\GamingTweaker\Assets\cles-release.txt'
$entropie = [Text.Encoding]::UTF8.GetBytes('GamingTweaker-release')
$utf8 = New-Object Text.UTF8Encoding($false)

function Enregistrer-Cle([string]$xmlPrive) {
    $dossier = Split-Path -Parent $CleFichier
    New-Item -ItemType Directory -Force -Path $dossier | Out-Null
    $chiffre = [Security.Cryptography.ProtectedData]::Protect($utf8.GetBytes($xmlPrive), $entropie, 'CurrentUser')
    [IO.File]::WriteAllBytes($CleFichier, $chiffre)
}

function Lire-Cle {
    if (-not (Test-Path $CleFichier)) { throw "Aucune cle privee dans $CleFichier : lancez -Generer (premiere fois) ou -Importer <sauvegarde>." }
    $clair = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($CleFichier), $entropie, 'CurrentUser')
    return $utf8.GetString($clair)
}

function Nouveau-Rsa([string]$xml) {
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
    $rsa.PersistKeyInCsp = $false
    $rsa.FromXmlString($xml)
    if ($rsa.KeySize -lt 3072) { throw "Cle de $($rsa.KeySize) bits : 3072 au minimum." }
    return $rsa
}

function Cle-Publique-Connue([string]$publique) {
    if (-not (Test-Path $fichierCles)) { return $false }
    return @([IO.File]::ReadAllLines($fichierCles, $utf8) | ForEach-Object { $_.Trim() }) -contains $publique
}

if ($Generer) {
    if (Test-Path $CleFichier) { throw "Une cle existe deja ($CleFichier). Supprimez-la seulement si vous etes sur de vouloir en changer." }
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(3072)
    $rsa.PersistKeyInCsp = $false
    Enregistrer-Cle $rsa.ToXmlString($true)
    $publique = $rsa.ToXmlString($false)
    [IO.File]::AppendAllText($fichierCles, $publique + "`n", $utf8)
    Write-Host "Cle creee : $CleFichier (chiffree pour ce compte Windows)." -ForegroundColor Green
    Write-Host "Cle publique ajoutee a $fichierCles : commitez ce fichier, il sera dans la prochaine version."
    Write-Host "Sauvegardez maintenant la cle privee hors ligne : -Exporter E:\cle-gamingtweaker.xml (cle USB rangee a part)." -ForegroundColor Yellow
    exit 0
}

if ($Exporter) {
    $xml = Lire-Cle
    [IO.File]::WriteAllText($Exporter, $xml, $utf8)
    Write-Host "Cle privee ecrite EN CLAIR dans $Exporter. Gardez ce fichier hors ligne, jamais dans le depot ni dans un cloud." -ForegroundColor Yellow
    exit 0
}

if ($Importer) {
    if (Test-Path $CleFichier) { throw "Une cle existe deja ($CleFichier)." }
    $xml = [IO.File]::ReadAllText($Importer, $utf8).Trim()
    if ($xml -notmatch '<D>') { throw 'Ce fichier ne contient pas de cle privee.' }
    $rsa = Nouveau-Rsa $xml
    Enregistrer-Cle $xml
    if (-not (Cle-Publique-Connue $rsa.ToXmlString($false))) { Write-Warning "Sa cle publique n'est pas dans $fichierCles." }
    Write-Host "Cle importee : $CleFichier." -ForegroundColor Green
    exit 0
}

if (-not $Tag) { throw 'Precisez -Generer, -Exporter, -Importer ou -Tag vX.Y.Z.' }
if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "Tag $Tag : la forme attendue est vX.Y.Z." }
$version = $Matches[1]

$rsa = Nouveau-Rsa (Lire-Cle)
$publique = $rsa.ToXmlString($false)
if (-not (Cle-Publique-Connue $publique)) {
    Write-Warning "La cle publique de cette cle privee n'est pas dans $fichierCles : les versions deja installees ne pourront pas verifier la signature."
}

$entetes = @{ 'User-Agent' = 'GamingTweaker-signature' }
$rel = Invoke-RestMethod "https://api.github.com/repos/$depot/releases/tags/$Tag" -Headers $entetes
if ($rel.draft -or $rel.prerelease) { throw "$Tag est un brouillon ou une pre-version : seules les releases sont signees." }
$asset = @($rel.assets | Where-Object { $_.name -eq 'GamingTweaker.exe' }) | Select-Object -First 1
if (-not $asset) { throw "La release $Tag ne contient pas GamingTweaker.exe (build encore en cours ?)." }
if ($asset.browser_download_url -notlike "https://github.com/$depot/releases/download/*") { throw 'Adresse de telechargement inattendue.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('gt-signature-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $exe = Join-Path $temp 'GamingTweaker.exe'
    Write-Host "Telechargement de GamingTweaker.exe ($Tag)..."
    Invoke-WebRequest $asset.browser_download_url -OutFile $exe -UseBasicParsing -Headers $entetes
    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()

    # Controles : empreinte de GitHub et de la CI, version de l'exe, commit du tag identique au depot local.
    $controles = 0
    if ([string]$asset.digest -match '^sha256:([0-9a-fA-F]{64})$') {
        if ($Matches[1].ToLowerInvariant() -ne $hash) { throw 'Le fichier telecharge ne correspond pas a l''empreinte calculee par GitHub.' }
        $controles++
    }
    if ([string]$rel.body -match 'SHA-256 de GamingTweaker\.exe\s*:\s*`?([0-9A-Fa-f]{64})') {
        if ($Matches[1].ToLowerInvariant() -ne $hash) { throw 'Le fichier telecharge ne correspond pas a l''empreinte ecrite par la CI dans les notes.' }
        $controles++
    }
    if ($controles -eq 0) { throw 'Aucune empreinte publiee pour cet exe : rien n''a ete signe.' }
    $fv = (Get-Item $exe).VersionInfo.FileVersion
    if (-not ("$fv" -like "$version.*")) { throw "L'exe est en version $fv, pas $version." }

    $commit = (Invoke-RestMethod "https://api.github.com/repos/$depot/commits/$Tag" -Headers $entetes).sha
    $local = $null
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $local = (& git -C $racine rev-parse "$Tag^{commit}" 2>$null)
        if ($LASTEXITCODE -ne 0) { $local = $null }
    }
    if ($local -and $local.Trim() -ne $commit) { throw "Le tag $Tag pointe sur $commit sur GitHub mais sur $local dans votre depot local : rien n'a ete signe." }

    Write-Host ''
    Write-Host "Version      : $version (fichier $fv)"
    Write-Host "SHA-256      : $hash"
    $origine = if ($local) { ' (identique a votre depot local)' } else { ' (depot local non verifie : faites git fetch --tags)' }
    Write-Host ("Commit du tag: $commit" + $origine)
    Write-Host "Signe        : $($rel.html_url)"
    Write-Host ''
    Write-Host 'Ne signez que si ce commit est bien le votre et que le build de ce tag est vert.' -ForegroundColor Yellow
    if ((Read-Host 'Signer cette version ? Tapez OUI') -cne 'OUI') { Write-Host 'Rien n''a ete signe.'; exit 1 }

    $message = $utf8.GetBytes("GamingTweaker.exe|$version|$hash")
    $signature = $rsa.SignData($message, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    if (-not $rsa.VerifyData($message, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'La signature produite ne se verifie pas.' }
    $sig = Join-Path $temp 'GamingTweaker.exe.sig'
    [IO.File]::WriteAllText($sig, [Convert]::ToBase64String($signature), [Text.Encoding]::ASCII)

    if (Get-Command gh -ErrorAction SilentlyContinue) {
        & gh release upload $Tag $sig --repo $depot --clobber
        if ($LASTEXITCODE -ne 0) { throw 'Envoi de la signature impossible (gh auth login ?).' }
        Write-Host "Signature jointe a la release $Tag : la mise a jour est maintenant proposee aux utilisateurs." -ForegroundColor Green
    } else {
        $copie = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GamingTweaker.exe.sig'
        Copy-Item $sig $copie -Force
        Write-Host "Signature ecrite dans $copie. Deposez ce fichier sur la page de la release (Edit, puis Attach binaries) : $($rel.html_url)" -ForegroundColor Green
    }
} finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    $rsa.Dispose()
}
