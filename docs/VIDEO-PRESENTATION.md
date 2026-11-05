# Vidéo de présentation de Rinel

Statut : **produite le 05/11/2026**, sans voix off (décision de l'auteur du fork). Le sens passe par
le texte à l'écran, la lecture se fait aussi sans le son.

| Fichier | Contenu |
|---|---|
| [`video/rinel-presentation-60s.mp4`](video/rinel-presentation-60s.mp4) | version complète, 60 s, 1920 × 1080, 30 i/s, musique |
| [`video/rinel-presentation-22s.mp4`](video/rinel-presentation-22s.mp4) | version courte, 22 s (arrivée, réponse dans l'encoche, deux interfaces, final) |
| [`video/vignette.png`](video/vignette.png) | image d'ouverture |
| [`video/source/`](video/source) | de quoi refaire la vidéo (script, sprites) |

## Style visuel : verre cristal en 3D simulée

- Fond profond bleu nuit avec des halos lumineux (bokeh) qui dérivent à des vitesses différentes, pour la profondeur.
- Panneaux de **verre dépoli** : le fond se floute derrière, biseau lumineux, reflet courbe en haut, bande spéculaire qui balaie,
  ombre portée ; ils pivotent légèrement en perspective (vrai calcul de perspective, pas un simple décalage).
- **Orbes de cristal** pour Rinel : effet de bord lumineux (Fresnel), reflets, lueur colorée et reflet sur le sol.
- Les captures de l'encoche sont détourées (l'encoche seule) et posées sous le verre.
- Rendu image par image avec Pillow et NumPy, sans moteur 3D ; environ 1,3 s par image.

## Scénario (60 s)

| Temps | Scène | Texte à l'écran |
|---|---|---|
| 0:00-0:05 | Le problème : un terminal attend une réponse cachée derrière une autre fenêtre, le compteur d'attente monte | « Il attend. Personne ne le sait. » |
| 0:05-0:12 | Rinel arrive : l'encoche descend, Rinel fait coucou puis se range avec ses trois frères dans l'encoche | « Rinel · L'encoche Claude Code pour Windows », « Un petit chevalier par session » |
| 0:12-0:22 | Les quatre états : en cours (flotte), prêt (saute), il t'attend (tremble), erreur (s'affaisse) | « Un coup d'œil suffit », étiquettes colorées |
| 0:22-0:34 | Répondre dans l'encoche : repliée, entrouverte, carte de permission, clic sur Autoriser | « Réponds dans l'encoche, sans changer de fenêtre » |
| 0:34-0:44 | Les deux interfaces : la même question dans Claude Desktop et dans l'encoche ; on répond d'un côté, l'autre se ferme, puis l'inverse | « Dans l'encoche et dans Claude Desktop : la première réponse gagne » |
| 0:44-0:52 | La confiance : rapport d'audit qui défile, carte « en faire une règle ? » | « Tout ce que font les agents est journalisé » |
| 0:52-1:00 | Final : Rinel fait coucou, adresse du dépôt | « github.com/ATX-AI-Dev/bip · Fork de CR0ZER/bip · licence MIT » |

## Contenu et droits

- Tout est fictif : sessions `data-pipeline`, `api-gateway`, `landing-page`, rapport d'audit sur des données
  de démonstration, aucun nom de client ni chemin personnel.
- La fenêtre « Claude Desktop » est un dessin générique, sans logo.
- **Musique originale**, synthétisée par le script (cordes pincées façon luth, accords en ré dorien) : aucun
  morceau tiers, aucune licence à vérifier. Aucun extrait de *Kaamelott*.

## Refaire la vidéo

Prérequis : Python avec Pillow et NumPy, ffmpeg. Depuis `docs/video/source/` :

```bat
python make_video.py frames            rem 1 800 images dans frames\ (environ 3 min)
python make_video.py audio             rem audio.wav
ffmpeg -framerate 30 -i frames/%05d.png -i audio.wav -c:v libx264 -crf 18 -pix_fmt yuv420p -c:a aac -movflags +faststart -shortest rinel-presentation-60s.mp4
```

Les captures viennent de `docs/screenshots/` ; les sprites de Rinel (`source/sprites/`) sont les SVG de
`design/rinel-mascotte/svg/` rendus en 512 px par Chrome. La version courte est un montage des secondes
5-10, 26-32, 36-42 et 55-60 de la version complète.

## Pistes pour la suite

- Sous-titres anglais incrustés ou fichier `.srt` si la vidéo est diffusée hors du monde francophone.
- Version carrée 1080 × 1080 : la composition actuelle est prévue pour le 16:9, elle demande une mise en
  page dédiée.
