if (-not (Set-TweakDevicePowerKind -Kind Nic -Enabled $false)) { Skip-GTItem 'Aucune carte réseau physique à gestion d''alimentation sur cette machine.' }
