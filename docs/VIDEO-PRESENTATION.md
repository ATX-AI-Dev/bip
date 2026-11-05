# Vidéo de présentation de Rinel : plan de production

Statut : **prévue, pas encore produite**. Ce document fixe le scénario, les visuels et la méthode,
pour pouvoir la fabriquer d'une traite.

## Objectif

En 60 secondes, faire comprendre à un développeur qui utilise Claude Code :

1. le problème : une session attend une réponse et personne ne le voit ;
2. la solution : un petit chevalier par session, dans une encoche, qui dit qui travaille, qui a fini, qui attend ;
3. ce qui la distingue : on répond sans changer de fenêtre, **dans l'encoche ou dans Claude Desktop, au choix** ;
4. la confiance : un journal d'audit chaîné, des règles que l'on crée soi-même, rien d'envoyé sur le réseau.

Public : développeurs Windows qui lancent plusieurs sessions Claude Code. Ton : posé, un peu
malicieux (clin d'œil à Kaamelott, sans jamais en reprendre un extrait).

## Format

| | |
|---|---|
| Durée | 60 s (version courte 20 s pour les réseaux) |
| Cadre | 1920 × 1080, 30 i/s ; recadrage 1080 × 1080 pour la version carrée |
| Son | voix off en français, musique discrète, 3 bruitages ; sous-titres incrustés (visionnage sans son) |
| Langues | français d'abord, sous-titres anglais ensuite |
| Fond | dégradé bleu-gris des captures du README, encoche noire en haut au centre |

## Scénario

| Temps | Scène | Image | Voix off (français) | Texte à l'écran |
|---|---|---|---|---|
| 0:00-0:05 | Le problème | Fenêtre de terminal qui défile, curseur qui clignote ; un bandeau « Autoriser cette commande ? » caché derrière d'autres fenêtres | « Claude Code travaille seul. Et puis il s'arrête : il attend une réponse que tu n'as pas vue. » | « Il attend. Personne ne le sait. » |
| 0:05-0:12 | Arrivée de Rinel | L'encoche descend du haut de l'écran ; `rinel-coucou.svg` fait coucou, puis se replie en quatre petits chevaliers | « Voici Rinel. Un petit chevalier par session, collé en haut de l'écran. » | « Rinel · l'encoche Claude Code pour Windows » |
| 0:12-0:22 | Les quatre états | Planche des états : orange (en cours, flotte), vert (prêt, petit saut), rouge (attend, parchemin brandi), gris (erreur, plume tombante) | « Orange, il travaille. Vert, il a fini. Rouge, il t'attend. Tu vois tout d'un coup d'œil. » | Les 4 couleurs étiquetées |
| 0:22-0:34 | Répondre dans l'encoche | Entrouverture « Migrations staging attend ta réponse », clic sur l'encoche, carte de permission, clic sur **Autoriser** ; le chevalier repasse à l'orange | « Une permission ? Une question ? Tu réponds dans l'encoche, sans changer de fenêtre. » | « Autoriser · Refuser · Répondre dans Claude » |
| 0:34-0:44 | Les deux interfaces | Écran partagé : la question s'affiche à gauche dans Claude Desktop, à droite dans l'encoche ; on répond d'un côté, l'autre disparaît aussitôt | « Et si tu préfères Claude Desktop, la même question y est aussi. La première réponse gagne. » | « Dans l'encoche **et** dans Claude Desktop » |
| 0:44-0:52 | La confiance | Rapport d'audit (capture `rapport-audit.png`) qui défile ; la carte « Tu as autorisé npm test 5 fois : en faire une règle ? » | « Tout ce que font les agents est journalisé, chaîné, et tu crées tes propres règles. Rien ne quitte ton poste. » | « Journal d'audit · règles suggérées · 100 % local » |
| 0:52-1:00 | Final | Rinel fait coucou sur fond bleu-gris, logo et adresse du dépôt | « Rinel. L'encoche qui veille sur tes sessions. » | « github.com/ATX-AI-Dev/bip · fork de CR0ZER/bip · MIT » |

## Visuels

| Visuel | État | Où |
|---|---|---|
| Rinel animé (coucou) et les 5 états en SVG animés | existe | `design/rinel-mascotte/svg/` |
| Planche des états (128, 40, 20, 16 px) | existe | `design/rinel-mascotte/apercu-planche.png` |
| Captures de l'encoche : repliée, entrouverte, permission, question, commandes, sous-agents, réglages, encoche vide, règle, rapport d'audit | existent | `docs/screenshots/` |
| Fenêtre Claude Desktop avec la même question (scène 0:34) | **à capturer** avec des sessions fictives, sans donnée réelle | à produire |
| Terminal qui défile (scène 0:00) | **à créer** en HTML/CSS, texte fictif | à produire |
| Logo de fin et fond dégradé | **à créer** | à produire |

Règle de contenu : uniquement des sessions fictives (noms comme `data-pipeline`, `api-gateway`),
aucun nom de client, aucun chemin personnel, aucune extraction audio ou vidéo de *Kaamelott*.

## Méthode de fabrication

1. **Scènes en HTML/CSS/SVG** : une page par scène, animée par une chronologie CSS ou JavaScript
   pilotée par le temps (les SVG de Rinel sont déjà animés en CSS).
2. **Enregistrement image par image** : Chrome sans interface prend une capture par image (30 par
   seconde) en avançant l'horloge de la page, pour un rendu net et reproductible.
3. **Assemblage** : ffmpeg (images → vidéo H.264, ajout de la voix off et de la musique, incrustation
   des sous-titres). *À vérifier au moment de produire : ffmpeg installé, sinon le télécharger.*
4. **Voix off** : enregistrée par une personne, ou voix de synthèse le temps du montage ; texte
   ci-dessus, environ 130 mots.
5. **Musique** : morceau libre de droits (licence vérifiée et notée dans ce fichier).
6. **Contrôle** : visionnage sans son (les textes à l'écran suffisent-ils ?), sur un écran de
   téléphone, et relecture croisée du texte par un second agent.

## Livrables

- `rinel-presentation-60s.mp4` (1920 × 1080) et `rinel-presentation-20s.mp4` ;
- version carrée 1080 × 1080 ;
- fichier de sous-titres `.srt` français et anglais ;
- vignette (image d'ouverture) pour GitHub ;
- sources des scènes dans `docs/video/` pour pouvoir refaire la vidéo à chaque version.

## À décider avant de produire

- Voix humaine ou synthétique, et quelle musique.
- Où publier : dépôt GitHub (README, release), YouTube, réseaux.
- Version anglaise complète ou sous-titres seulement.
