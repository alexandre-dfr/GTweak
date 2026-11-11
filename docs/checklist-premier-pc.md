# Liste de contrôle du premier PC fixe

À passer sur le PC de jeu réel, exe en Release, session administrateur. Ces points ne peuvent pas être vérifiés dans Windows Sandbox (pas de GPU, pas de `winget`, matériel absent). Avant de commencer : créer un point de restauration manuel. Chaque ligne donne l'action, le résultat attendu et l'annulation.

## 1. Démarrage

| Action | Résultat attendu | Annulation |
|---|---|---|
| Lancer l'exe (UAC accepté) | Interface en français, détection des options sans erreur | Fermer |
| Menu Informations : demander les capteurs | Capteurs chargés seulement à la demande ; GPU, disques, ventilateurs listés | Fermer |

## 2. Pilote NVIDIA

| Action | Résultat attendu | Annulation |
|---|---|---|
| Rechercher le pilote NVIDIA (menu Mises à jour) | Version la plus récente trouvée pour le GPU détecté ; en cas d'échec de la recherche, ouverture de la page officielle | - |
| Installer le pilote | Installateur téléchargé, signature vérifiée, lancé avec `-s -noreboot` ; le fichier n'est pas supprimé tant que l'installateur tourne ; redémarrage signalé, jamais automatique | Désinstaller depuis « Applications installées » (non réversible par l'outil) |
| Fournir un installateur à signature invalide (fichier modifié) | Fichier supprimé, installation refusée, échec au rapport | - |

## 3. Windows Update, winget, nettoyage

| Action | Résultat attendu | Annulation |
|---|---|---|
| Installer les mises à jour Windows | Mises à jour installées, rapport clair, pas de redémarrage automatique | Désinstaller une mise à jour dans Paramètres |
| `winget upgrade` depuis l'outil (session élevée) | Paquets mis à jour, ou « ignoré » si `winget` est absent ; pas de blocage | Réinstaller l'ancienne version à la main |
| Supprimer Windows.old, élément par élément | `takeown` effectué par élément, dossier supprimé, taille de l'aperçu cohérente | Non réversible (indiqué dans l'aperçu) |
| Supprimer un pilote utilisé avec `pnputil` | Code 0xE000020F signalé comme « en cours d'utilisation », pas comme échec grave | - |
| Nettoyer les orphelins `ProductsEx` (élevé) | Entrées listées puis traitées sans erreur d'accès | Historique : annuler l'option |

## 4. Capteurs et PawnIO

| Action | Résultat attendu | Annulation |
|---|---|---|
| Capteurs sans PawnIO | Température CPU indiquée comme indisponible, message clair ; GPU et disques s'affichent | - |
| Installer PawnIO (`namazso.PawnIO` via winget), rouvrir les capteurs | Température et tension CPU affichées ; LibreHardwareMonitor chargé depuis les DLL embarquées de l'exe (aucune DLL à côté) | Désinstaller PawnIO depuis « Applications installées » |

## 5. Options Testable=false (matériel)

Options concernées : économie d'énergie USB, économie d'énergie carte réseau, HAGS, PCIe ASPM, Windows.old, anciens pilotes, et toute option marquée `testable: false` dans le catalogue.

| Action | Résultat attendu | Annulation |
|---|---|---|
| Appliquer une option à la fois (jamais toutes d'un coup la première fois), vérifier la détection | « Appliquée » ; ni écran noir ni perte réseau ; redémarrage signalé si nécessaire | Historique : annuler l'option, redémarrer, vérifier « Non appliquée » |
| Redémarrer et jouer 15 minutes | Stabilité, pas de régression de FPS | Annuler l'option fautive |
| Options sur des services (`sc config ... start= auto`) | Le démarrage différé est retiré quand l'option le demande ; détection cohérente | Le type de démarrage d'origine, différé compris, est rétabli |
| Charge processeur des analyses Defender avec protection contre les falsifications active | Échec explicite si Windows refuse, jamais un faux « Appliquée » | - |
| `cln.temp` et `upd.apps` (winget présent) | Fichiers temporaires de l'utilisateur supprimés (jamais le dossier Run de l'outil) ; mises à jour winget installées | - |

## 6. Annulation réelle

| Action | Résultat attendu | Annulation |
|---|---|---|
| Appliquer 3 à 5 options réversibles, puis annuler l'une depuis l'historique | Valeurs d'origine rétablies ; les autres options restent appliquées | - |
| Modifier à la main la valeur d'une option appliquée, puis l'annuler | Conflit signalé avant écrasement | - |
| « Tout restaurer » | Toutes les options de l'outil rétablies ; redétection : toutes « Non appliquées » | Rejouer le profil |
| Tuer l'application en pleine exécution, puis relancer | Proposition d'annuler la session interrompue | Accepter l'annulation |

## 7. Mode Jeu

| Action | Résultat attendu | Annulation |
|---|---|---|
| Activer le Mode Jeu | Services et processus choisis arrêtés, plan d'alimentation changé ; service avec dépendants refusé avec une raison | **Rétablir** : services relancés, plan d'origine restauré |
| Fermer l'outil sans rétablir, puis le relancer | Mode Jeu en cours détecté et rétablissable | Rétablir |

## 8. Routine planifiée

| Action | Résultat attendu | Annulation |
|---|---|---|
| Choisir un profil, **Planifier** | Tâche `\GamingTweaker\Routine` visible (`schtasks /Query /TN "\GamingTweaker\Routine"`) ; copie de l'exe dans le sous-dossier `App` du dossier de données protégé de GamingTweaker | **Supprimer la routine** |
| `icacls` sur le dossier de données protégé de GamingTweaker | Propriétaire Administrateurs ; écriture réservée aux Administrateurs et à SYSTÈME | - |
| Exécuter la tâche (`schtasks /Run`) | Rapport écrit dans le dossier de données protégé, code de sortie 0, aucune interface | - |
| Lancer l'interface pendant la routine (ou l'exe deux fois) | La seconde exécution refuse (code 2 en routine) | - |
| `GamingTweaker.exe --profile absent.json --silent` | Message clair, code 2 | - |

## 9. Windows 10 (si une machine ou une image est disponible)

| Action | Résultat attendu | Annulation |
|---|---|---|
| Lancer l'exe (1903 ou plus récent) | Démarrage normal ; options propres à Windows 11 absentes | Fermer |
| Appliquer puis annuler 3 options courantes | Même comportement que sous Windows 11 | Historique |

## 10. Conclusion

Noter pour chaque ligne : réussi, échec ou sans objet, avec le `report.txt` de la session. Tout échec est signalé avec l'identifiant de l'option.
