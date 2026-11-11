if (($GT_SelectedIds -contains 'opt.plan-ultimate') -or ((Get-GTActiveScheme) -ieq 'e9a42b02-d5df-448d-aa00-03f14749eb61')) { Skip-GTItem 'Le plan Performances ultimes est sélectionné ou déjà actif : le plan Performances élevées n''est pas appliqué.' }
$g = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
Initialize-GTScheme -Guid $g
Set-TweakActiveScheme -Guid $g
