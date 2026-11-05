$gtVide = $false
try {
    $gtBin = (New-Object -ComObject Shell.Application).NameSpace(0xA)
    $gtVide = ($null -ne $gtBin -and @($gtBin.Items()).Count -eq 0)
} catch { }
if ($gtVide) { Skip-GTItem 'La Corbeille est déjà vide.' }
Clear-RecycleBin -Force -ErrorAction Stop
