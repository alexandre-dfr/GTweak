<#
.SYNOPSIS
  Generates the winget manifests (format 1.6.0) of a published stable GamingTweaker release.

.DESCRIPTION
  Downloads GamingTweaker.exe from release vX.Y.Z, computes its SHA-256 and fills the templates of tools\winget\modele
  into tools\winget\manifests\a\alexandre-dfr\GamingTweaker\X.Y.Z\ (layout expected by microsoft/winget-pkgs).
  Submit with a pull request to https://github.com/microsoft/winget-pkgs, or "wingetcreate submit <folder>"
  (https://github.com/microsoft/winget-create). Later versions:
  "wingetcreate update alexandre-dfr.GamingTweaker -u <exe url> -v X.Y.Z --submit".
  Nothing is submitted by this script. ASCII only: Windows PowerShell 5.1 reads BOM-less UTF-8 scripts as ANSI;
  the French text lives in the UTF-8 templates, read explicitly as UTF-8.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\winget\generer-manifeste.ps1 -Version 0.9.3
#>
param(
  [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version
)
$ErrorActionPreference = 'Stop'
$url = "https://github.com/alexandre-dfr/GTweak/releases/download/v$Version/GamingTweaker.exe"

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("gt-winget-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
  $exe = Join-Path $tmp 'GamingTweaker.exe'
  Write-Host "Download: $url"
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing
  $sha = (Get-FileHash -Algorithm SHA256 $exe).Hash.ToUpperInvariant()
  Write-Host "SHA-256: $sha"
} finally {
  Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

$modele = Join-Path $PSScriptRoot 'modele'
$dossier = Join-Path $PSScriptRoot "manifests\a\alexandre-dfr\GamingTweaker\$Version"
New-Item -ItemType Directory -Force $dossier | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($f in Get-ChildItem $modele -Filter *.yaml) {
  $texte = [IO.File]::ReadAllText($f.FullName, $utf8).Replace('{{VERSION}}', $Version).Replace('{{SHA256}}', $sha)
  [IO.File]::WriteAllText((Join-Path $dossier $f.Name), $texte, $utf8)
}
Write-Host "Manifests written to $dossier"
Write-Host "Check (Windows with winget): winget validate --manifest `"$dossier`""
