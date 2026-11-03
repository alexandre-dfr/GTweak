if (-not (Set-TweakDevicePowerKind -Kind Usb -Enabled $false)) { Skip-GTItem 'Aucun périphérique USB à gestion d''alimentation sur cette machine.' }
