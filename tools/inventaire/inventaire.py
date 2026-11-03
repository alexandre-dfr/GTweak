"""Inventaire des sources du catalogue GamingTweaker (Task 7B).

Lit les copies figées de WinUtil (config/tweaks.json) et de privacy.sexy
(src/application/collections/windows.yaml) et produit un inventaire
lisible par machine : une entrée par tweak WinUtil et une entrée par
« sous-catégorie feuille » privacy.sexy (catégorie dont les enfants directs
comprennent des scripts ; les catégories parentes forment le groupe).

Les fichiers sources sont traités comme des données : rien n'y est exécuté.

Usage :
    python -I tools/inventaire/inventaire.py [--sources DIR] [--out FICHIER]
        [--squelette FICHIER]

--squelette écrit le squelette du tableau de couverture (décision vide).

Prérequis : PyYAML (6.x) installé dans l'interpréteur de base, par exemple
`python -m pip install pyyaml` lancé en administrateur ou dans une
installation Python propre. L'option -I (mode isolé) ignore le dossier
site de l'utilisateur et les variables PYTHON*, donc un `pip install --user`
ne suffit pas.
"""
import argparse
import collections
import json
import os
import sys

import yaml

ICI = os.path.dirname(os.path.abspath(__file__))

NIVEAU_PS = {"standard": "Sur", "strict": "Avance", None: "Risque"}


def lire_winutil(chemin):
    with open(chemin, encoding="utf-8-sig") as f:
        # strict=False : le fichier contient des tabulations brutes dans des chaînes.
        donnees = json.loads(f.read(), strict=False)
    entrees = []
    for cle, v in donnees.items():
        categorie = v.get("category", "")
        if categorie.startswith("Essential"):
            niveau = "Essential"
        elif "Advanced" in categorie:
            niveau = "Advanced"
        else:
            niveau = categorie
        entrees.append({
            "source": "WinUtil",
            "cle": cle,
            "entree": v.get("Content", cle),
            "categorie": categorie,
            "niveau": niveau,
            "type": v.get("Type", "Checkbox"),
            "description": v.get("Description", ""),
            "services": [s.get("Name") for s in v.get("service", []) or []],
            "registre": ["{}\\{}".format(r.get("Path"), r.get("Name")) for r in v.get("registry", []) or []],
        })
    return entrees


def _feuilles(noeud, chemin, sortie):
    enfants = noeud.get("children")
    if enfants is None:
        return
    nom = noeud["category"]
    scripts = [c for c in enfants if "children" not in c]
    if scripts:
        reco = collections.Counter(s.get("recommend") for s in scripts)
        # Niveau le plus fréquent ; en cas d'égalité, le plus prudent (none > strict > standard).
        ordre = {None: 0, "strict": 1, "standard": 2}
        dominant = sorted(reco.items(), key=lambda kv: (-kv[1], ordre[kv[0]]))[0][0]
        sortie.append({
            "source": "privacy.sexy",
            "groupe": " > ".join(chemin),
            "entree": nom,
            "niveau": dominant or "aucune",
            "niveau_gt": NIVEAU_PS[dominant],
            "scripts": len(scripts),
            "recommandations": {str(k or "aucune"): n for k, n in reco.items()},
            "noms_scripts": [s.get("name") for s in scripts],
        })
    for c in enfants:
        if "children" in c:
            _feuilles(c, chemin + [nom], sortie)


def lire_privacysexy(chemin):
    with open(chemin, encoding="utf-8") as f:
        donnees = yaml.safe_load(f)
    sortie = []
    for action in donnees["actions"]:
        _feuilles(action, [], sortie)
    return sortie


def squelette(winutil, ps):
    lignes = [
        "| source | entrée | niveau d'origine | décision | raison |",
        "|---|---|---|---|---|",
    ]
    for e in winutil:
        lignes.append("| WinUtil | {} ({}) | {} |  |  |".format(e["entree"], e["cle"], e["niveau"]))
    for e in ps:
        entree = "{} > {}".format(e["groupe"], e["entree"]) if e["groupe"] else e["entree"]
        lignes.append("| privacy.sexy | {} | {} |  |  |".format(entree.replace("|", "/"), e["niveau"]))
    return "\n".join(lignes) + "\n"


def main(argv):
    p = argparse.ArgumentParser()
    p.add_argument("--sources", default=os.path.join(ICI, "sources"))
    p.add_argument("--out", default=os.path.join(ICI, "inventaire.json"))
    p.add_argument("--squelette")
    a = p.parse_args(argv)

    winutil = lire_winutil(os.path.join(a.sources, "tweaks.json"))
    ps = lire_privacysexy(os.path.join(a.sources, "windows.yaml"))
    resume = {
        "winutil": len(winutil),
        "winutil_par_niveau": dict(collections.Counter(e["niveau"] for e in winutil)),
        "privacysexy_feuilles": len(ps),
        "privacysexy_scripts": sum(e["scripts"] for e in ps),
        "privacysexy_par_niveau": dict(collections.Counter(e["niveau"] for e in ps)),
        "privacysexy_par_categorie": dict(collections.Counter(
            (e["groupe"] or e["entree"]).split(" > ")[0] for e in ps)),
    }
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"resume": resume, "winutil": winutil, "privacysexy": ps}, f, ensure_ascii=False, indent=1)
    if a.squelette:
        with open(a.squelette, "w", encoding="utf-8", newline="\n") as f:
            f.write(squelette(winutil, ps))
    print(json.dumps(resume, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
