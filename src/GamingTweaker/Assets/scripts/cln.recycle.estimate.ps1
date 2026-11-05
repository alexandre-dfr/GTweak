$gtBin = (New-Object -ComObject Shell.Application).NameSpace(0xA)
$gtTotal = [long]0
foreach ($gtItem in @($gtBin.Items())) { $gtTotal += [long]$gtItem.Size }
$gtTotal
