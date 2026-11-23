"""Vérifie les règles de base des fichiers texte suivis par git (voir .editorconfig).

Usage : python -I tools/style.py         (code de sortie 1 si une règle n'est pas respectée)
        python -I tools/style.py --fix   (corrige BOM, espaces en fin de ligne et dernière ligne)

Règles : UTF-8 sans BOM, pas d'espace ni de tabulation en fin de ligne, pas d'indentation par tabulation,
fichier terminé par un saut de ligne, JSON valide. Les copies figées et les données de test sont ignorées.
"""
import json
import subprocess
import sys

EXTENSIONS = ('.cs', '.xaml', '.ps1', '.json', '.yml', '.yaml', '.csproj', '.md', '.py', '.wsb', '.editorconfig', '.gitattributes')
IGNORES = ('tools/inventaire/', 'tests/GamingTweaker.Tests/fixtures/')


def fichiers():
    sortie = subprocess.check_output(['git', 'ls-files', '-co', '--exclude-standard'])
    for f in sortie.decode('utf-8').splitlines():
        if f.endswith(EXTENSIONS) and not f.startswith(IGNORES):
            yield f


def verifier(chemin, corriger):
    erreurs = []
    with open(chemin, 'rb') as fh:
        brut = fh.read()
    if brut.startswith(b'\xef\xbb\xbf'):
        erreurs.append('BOM UTF-8')
        brut = brut[3:]
    try:
        texte = brut.decode('utf-8')
    except UnicodeDecodeError as e:
        return ['pas en UTF-8 (' + str(e) + ')']
    fin = '\r\n' if '\r\n' in texte else '\n'
    lignes = texte.split(fin)
    propres = []
    for n, ligne in enumerate(lignes, 1):
        if ligne != ligne.rstrip(' \t'):
            erreurs.append('ligne %d : espace en fin de ligne' % n)
        if ligne.startswith('\t'):
            erreurs.append('ligne %d : indentation par tabulation' % n)
        propres.append(ligne.rstrip(' \t'))
    if texte and not texte.endswith(fin):
        erreurs.append('pas de saut de ligne final')
        propres.append('')
    if chemin.endswith('.json'):
        try:
            json.loads(texte)
        except ValueError as e:
            erreurs.append('JSON invalide : ' + str(e))
    if corriger and erreurs:
        with open(chemin, 'wb') as fh:
            fh.write(fin.join(propres).encode('utf-8'))
    return erreurs


def main():
    corriger = '--fix' in sys.argv[1:]
    total = 0
    for f in fichiers():
        for e in verifier(f, corriger):
            print(f + ' : ' + e)
            total += 1
    if total and not corriger:
        print('%d problème(s). Corriger avec : python -I tools/style.py --fix' % total)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
