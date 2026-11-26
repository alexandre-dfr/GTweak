# Menu Installation et assistant après installation de Windows

## Intention
Après une installation de Windows, tout faire depuis GamingTweaker en répondant à des questions : installer launchers, logiciels
des marques et applications, puis régler Windows, dans un ordre qui a du sens (nettoyage à la fin, après mises à jour et installations).

## Décisions (validées)
- Trois catégories demandées + « Composants » (Visual C++, DirectX, .NET) ; ajout de Notepad++ et Deezer.
- Ordre d'exécution, aussi ordre des questions et de la barre latérale : Mises à jour → Bloatware → Installation → Télémétrie →
  Optimisation → Nettoyage (`Catalogue.OrdreMenus`, appliqué par `ScriptBuilder` avant le champ `ordre`).
- Téléchargement « facilité » : winget s'il est présent ; sinon lien direct de l'éditeur (https, dossier réservé aux administrateurs,
  fichier verrouillé, signature Authenticode valide et délivrée à l'éditeur attendu), installation silencieuse. Sans lien stable : page officielle dans le rapport.
- Pas de relance de l'optimisation du démarrage après les installations (l'utilisateur le fera plus tard).

## Construction
- `Assets/applis-list.json` : catalogue (id, catégorie, winget/msstore, url, type, args, éditeur, page, noms installés, marque).
  Identifiants winget vérifiés dans microsoft/winget-pkgs ; liens directs et options silencieuses repris des manifestes winget.
- 4 options `ins.<catégorie>` (liste `applis-<catégorie>`), non réversibles, non testables par le cycle réel.
- `AppsProvider` : masque les applis déjà installées (registre Uninstall), coche d'office les composants et les logiciels du matériel
  détecté (VID USB, fabricant de la carte mère, GPU, processeur Intel).
- `Install-GTAppSelection` (Common.ps1) : catalogue injecté dans le script (`$GT_AppCatalog`) ; seules ses clés sont installables.
- Assistant (`AssistantWindow`, `AssistantViewModel`) : coche les options de la fenêtre principale (mêmes objets, sans les options Risqué),
  récapitulatif, puis Aperçu et LANCER habituels.

## Hors champ / à vérifier sur un vrai PC
- Les options silencieuses des liens directs (Rockstar, EA, Opera GX, Parsec) viennent des manifestes winget, non vérifiées en réel.
- Discord et Spotify s'installent pour le compte qui lance l'appli (installateurs par utilisateur).
