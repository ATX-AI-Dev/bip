# Changelog

Les versions suivent [SemVer](https://semver.org/lang/fr/). Chaque section sert de notes à la release GitHub correspondante.

## [Non publié]

### Phase 4 « compagnon » : agendas (Google, Microsoft personnel, M365)

- Agendas lus par Logres sur `.178` par leur adresse iCal secrète (décision de Ludo, 30/10/2026 ; contrat `docs/CONTRAT-BIP-AGENDA.md` de Logres) ; les adresses ne quittent jamais `.178`.
- `bip agenda` : reçoit du collecteur la réponse brute du relevé (base64 sur l'entrée standard ; PowerShell 7 réécrirait les dates), revalide l'agenda et l'écrit dans `state\agenda.json`.
- Encoche repliée : réunion en cours ou prochaine du jour dans les 2 h ; entrouverture 5 min avant (une fois) ; section « Aujourd'hui » avec **Rejoindre** (Teams, Meet, Zoom) ; bilan du matin ; état des agendas et « ne pas déranger pendant les réunions » (désactivé par défaut).
- `bip agenda --vu` : Logres n'envoie l'agenda que lorsqu'il change ; le collecteur dit « rien de neuf » toutes les 5 min, ce qui rafraîchit la date de réception (sans cela, l'agenda était jugé périmé au bout de 30 min). Défaut trouvé à l'usage le 03/11/2026.
- 13 tests (207 au total).

### Phase 3 « compagnon » : quotidien (capture rapide, bilan du matin, musique)

- **Capture rapide** : raccourci global `Ctrl+Alt+B`, une ligne horodatée ajoutée à `ideation/inbox.md` du vault (copie locale Syncthing), créée avec son en-tête au besoin ; jamais de dossier créé ni d'écriture à travers un lien ; résultat annoncé d'une ligne.
- **Bilan du matin** dans le panneau déplié (5 h - 12 h) : ce qui s'est passé depuis la veille à 20 h (sessions, commandes, actions et refus des agents, alertes Logres) ; masquable jusqu'au lendemain ; recalculé toutes les 30 s au plus.
- Les infos de Bip Connect sont inscrites au journal du jour (`info2` / `info3`) ; le journal se lit sur plusieurs jours (le récap d'un appel qui passe minuit garde ce qui précède, constat F7 de l'audit).
- **Musique en cours** dans le panneau déplié (sessions média de Windows, sans SDK ni réseau) : titre, artiste, application, lecture/pause et morceau suivant. Vérifié avec une vraie vidéo YouTube dans Chrome.
- **Pause pendant les appels** : la musique qui joue est mise en pause au début d'un appel et reprend à la fin si le même morceau est toujours en pause (`music=`, réglable dans ☰).
- **Bilan du matin cliquable** : détail heure par heure (sessions finies, commandes, alertes ; 20 lignes au plus) et lien vers le rapport d'audit.
- 26 tests ajoutés (194 au total), dont écriture de la capture et journal sur plusieurs jours (Model.cs testé avec un `Program` factice). Relecture Gemini : 7 constats corrigés (note qui partait dans une réponse « Autre… » à un agent, texte perdu au redessin ou en cas d'échec d'écriture, panneau bloqué ouvert, lien orphelin suivi, bilan non redessiné, tests manquants).

### Phase 2 « compagnon » : Antigravity

- **Sessions Antigravity dans l'encoche** : « agy · titre · dossier/ », état (au repos, en cours, prête, erreur), titre tiré de la dernière demande, preuve de vie par le processus `agy`. Contrat des hooks vérifié dans la documentation officielle et par un essai réel (hook espion) le 28/10/2026.
- **Journal d'audit** : chaque outil utilisé par Antigravity (commande, résultat ou échec) est inscrit ; ses commandes comptent dans le récap de fin de tour et le rapport.
- **Ses demandes de permission restent dans Antigravity** : aucun de ses évènements ne signale une attente ; le hook répond toujours la décision neutre (`allow` : la politique normale d'Antigravity décide).
- **Installation sûre** : script relais `~/.gemini/config/bip-hook.cmd` hors du dossier de Bip et entrée `bip` dans `hooks.json`. Essai réel : un hook introuvable fait planter toute la tâche d'Antigravity (message de `cmd.exe` illisible en UTF-8) ; avec le relais, Bip absent, Antigravity continue normalement. Commande sans guillemets (Antigravity les échappe pour `cmd.exe`), chemin court si besoin.
- 12 tests ajoutés (168 au total).

### Phase 1 « compagnon » : fondations (fork personnel)

- **Format d'état commun** : `state\<id>.state` en JSON (`harness`, `state`, `dir`, `pid`, `proc`…), lu aussi dans l'ancien format. Chaque outil d'agent (claude, codex…) a son nom de processus, preuve que la session vit ; `NotchHook.exe … --harness <outil>` remonte jusqu'à ce processus. Les sessions d'un autre outil s'affichent « codex · dossier/ ».
- **Budget d'attention** : toutes les entrouvertures passent par une file à priorités (demande, puis fin de commande, puis résumé ou info). Avant, une fin de commande pouvait remplacer l'annonce d'une permission.
- **Rendre la main à Claude** : une demande sans réponse repart vers la fenêtre Claude au bout de 5, 10 (défaut), 30 ou 60 min (réglages), et tout de suite si le poste est verrouillé ou la session à distance déconnectée. Avant, la session pouvait attendre une heure.
- **Cartes de permission complètes** : fichier écrit, modifications et entrées d'outils montrés jusqu'à 400 lignes par bloc (avant : 12, 8 et 3 modifications) ; ce qui reste caché est compté et signalé en rouge.
- **Rapport d'audit** : caractères invisibles ou d'inversion (U+202E…) rendus visibles, comme dans l'encoche.
- **Canal Bip Connect en lecture seule** et commande `bip info` : afficher une info venue d'un autre programme, sans aucun pouvoir sur les demandes ni les sessions ; plafonds par source.
- L'identifiant de session est toujours assaini avant de servir de nom de fichier.
- 39 tests ajoutés (154 au total). Relecture de sécurité Gemini : commande shell sur plusieurs lignes comptée pour une ligne (avertissement jamais affiché, défaut déjà présent en v0.3.0), demande détachée de sa session si l'identifiant contient des caractères spéciaux, canal Bip Connect bloquable par des connexions muettes ou inondable en changeant de source, session liée à un shell intermédiaire, transcript réseau, sources usurpant Bip ou un agent : tous corrigés.

### Correctifs issus de l'audit du 23/10/2026 · *Fixes from the 2026-10-23 audit*

### Sécurité

- **Règles suggérées sur liste blanche** : seules les commandes connues (lanceurs de tests, sous-commandes de lecture, de compilation ou de vérification) deviennent une règle, réduite à leur sous-commande (`Bash(terraform plan:*)`). Avant, toute CLI d'un mot hors liste noire donnait une règle entière : 5 `terraform plan` proposaient `Bash(terraform:*)`, qui autorise aussi `terraform destroy` (de même pour `aws`, `az`, `gh`, `psql`, `find`, `sed`, `git branch`…).
  Exclus de la liste blanche après relecture : `git log` / `diff` / `show` (`--output` écrit n'importe quel fichier), `eslint` (`--fix`), `docker compose up` / `build` ; les scripts `npm run` n'acceptent que des suffixes connus (`test:e2e`, pas `build:deploy`).
- **Masquage des secrets** étendu : `mysql -p<mot de passe>` (y compris entre guillemets, sans toucher au port `-P3306`), `sshpass -p`, `docker login -p` / `az login -p`, clés privées PEM, webhooks Slack et Discord.
- **Outils MCP** : le journal d'audit ne garde plus leur entrée complète, seulement les noms des champs et les valeurs courtes ; un texte long ou sur plusieurs lignes, et tout champ de contenu même court (`content`, `body`, `message`, `query`…), est remplacé par sa taille.

### Corrections

- Le rapport d'audit ne plante plus sur une ligne dont le dossier contient un caractère de contrôle, et un échec s'affiche sous « Journal d'audit » dans les réglages au lieu de n'aller qu'au `notch.log`.
- Encoche repliée : une session qui attend ta réponse n'est plus cachée dans « +N » ; elle prend la place de la dernière session qui n'attend pas, sans changer l'ordre de lancement.
- `bip run "commande && autre"` : une commande entièrement entre guillemets (obligatoire sous PowerShell pour `&&`, `|`, `;`) est maintenant exécutée au lieu d'être prise pour un nom de programme ; un programme seul dont le chemin a des espaces (`"C:\Program Files\app.exe"`) garde ses guillemets.
- Un Bip qui revient dans l'encoche repliée pendant son fondu de sortie n'est plus dédoublé.
- Rapport d'audit : un seul à la fois, et le message d'échec s'efface au succès suivant.

### Tests

- 63 tests ajoutés (115 au total) : liste blanche des règles, nouveaux masquages, résumé des entrées MCP, dossier avec caractère de contrôle, choix des Bip de l'encoche repliée, `bip run` entre guillemets.

## [0.3.0] - 2026-10-14

Bip montre ce qui se passe dans une session : ses sous-agents, son nom, ce qu'elle a fait. · *Bip now shows what happens inside a session: its sub-agents, its name, what it did.*

### Nouveautés

- **Sous-agents visibles** dans l'encoche dépliée : sous chaque session, ses sous-agents en retrait, avec leur description et leur durée (5 au plus, puis « +N »). Un sous-agent fini passe au vert une minute, puis disparaît, y compris en arrière-plan. L'encoche repliée garde un seul Bip par session.
- **Titre du chat Claude** : chaque session s'affiche « titre · dossier/ » (« Migrations staging · data-pipeline/ ») ; l'entrouverture donne le titre seul. Plusieurs sessions dans un même dossier se distinguent enfin.
- **Récap de fin de tour** en vert sous une session prête : « 3 fichiers modifiés · 2 commandes · 1 échec ».
- **« Ne pas déranger » visible encoche dépliée**, en haut à gauche : « ☾ jusqu'à 17:18 » ou « ☾ appel en cours (Teams) » ; un clic le lève.
- **Rapport d'audit refait** : regroupé par session puis par agent (chaque sous-agent avec ses propres actions), synthèse en tête, filtres par session et par agent, nouveau style clair et sombre, commandes longues repliées.
- **Journal d'audit** : chaque action d'un sous-agent porte son identifiant.

### Corrections

- L'entrouverture s'élargit selon le nom de la session au lieu de le couper.
- Après un dépliage ou une entrouverture, les Bip cachés restaient animés : l'encoche consommait 2 à 3 fois plus (≈ 6 % d'un cœur au lieu de ≈ 2,3 %). Le panneau et l'entrouverture sont maintenant vidés une fois refermés.
- Les tests n'ajoutent plus d'entrées au registre (empreintes de fin de chaîne de leurs journaux temporaires).

### Performances

- Encoche : ~2,3 % d'un cœur avec une session au travail, y compris après des dépliages ; ~76 Mo.
- Hook : ~115 ms par appel, inchangé par rapport à la 0.2 malgré le suivi des sous-agents.

### Installation

- Deux nouveaux hooks (`SubagentStart`, `SubagentStop`) : relancer `installer.cmd` après la mise à jour.

### English summary

Sub-agents shown under each session in the open notch, sessions named after their Claude chat title, an end-of-turn recap, do-not-disturb shown in the open notch, and a redesigned audit report grouped by session and agent. Re-run `installer.cmd` to add the two new hooks.

## [0.2.0] - 2026-10-12

Bip suit aussi tes commandes, se fait discret pendant tes appels et garde la trace de ce que font les agents. · *Bip now follows your commands, keeps quiet during calls and records what agents do.*

### Nouveautés

- **`bip run <commande>`** : la commande tourne dans ton terminal et apparaît dans l'encoche ; Bip s'entrouvre à la fin avec la durée, ou le code de retour en cas d'échec. Le code de la commande est renvoyé.
- **Sourdine automatique pendant les appels** (Teams, Zoom, Meet, Slack, Discord, Webex…), détectée par l'utilisation du micro : bref signal au début, résumé de ce qui s'est passé à la fin. Désactivable dans les réglages.
- **Journal d'audit** de toutes les actions des agents : outil, objet, qui a autorisé, résultat. Secrets masqués avant écriture, chaîne d'empreintes SHA-256, 90 jours de conservation, vérification par `ClaudeNotch.exe --verify-audit`.
- **Rapport d'audit** depuis les réglages : page autonome avec filtres et export CSV.
- **Règles suggérées** après 5 autorisations de la même commande dans un projet, avec la règle exacte affichée ; portée « ce projet » ou « partout », retrait d'un clic depuis les réglages.
- **Réglages intégrés à l'encoche** : bouton ☰ en haut à droite (ou clic droit), à la place du menu contextuel qui passait sous l'encoche.
- **Croix** pour retirer une commande `bip run` de l'encoche.

### Sécurité

- Aucune suggestion de règle pour une commande composée, risquée ou trop large (interpréteurs, réseau, suppression, `git push`…).
- Fichier de réglages refusé s'il est un lien ; sauvegarde datée avant chaque modification.
- Export CSV protégé contre les formules.

### Performances

- Encoche : ~2,7 % d'un cœur avec une session au travail (contre ~4,7 % en 0.1.0) : Bips de l'encoche repliée à 8 images/s, vérification directe du processus Claude, souris suivie moins souvent quand l'encoche est repliée.
- Hook : −33 % par appel malgré le journal d'audit (lecteur JSON maison, empreinte par le fournisseur Windows).
- Journal d'audit relu de façon incrémentale : ~15 ms au lieu de ~250 ms sur une grosse journée.

### English summary

`bip run` to follow long commands in the notch, automatic mute during calls with an end-of-call recap, a tamper-evident audit log of every agent action with secret masking and an HTML/CSV report, suggested Claude Code rules after repeated approvals, and settings built into the notch. Lighter too: ~2.7 % of one core with a session working, hook 33 % faster.

## [0.1.0] - 2026-10-12

Première version publique. · *First public release.*

### Nouveautés

- **Encoche en haut au centre de l'écran** : trait quand rien ne tourne, languette avec un Bip par session (5 au plus, puis « +N »), forme entrouverte 4 s à chaque nouvelle demande, panneau déplié au survol. Repliée ou entrouverte, elle laisse passer les clics.
- **Bip**, la mascotte, en cinq états : en attente, en cours, prêt, erreur, au repos. Chacun a ses propres mouvements et se lit sans la couleur.
- **Permissions depuis l'encoche** : Autoriser, Refuser, ou « Toujours » avec sa portée affichée, limitée à la session. Jamais de « Toujours » pour les commandes shell.
- **Questions (AskUserQuestion)** depuis l'encoche : choix unique, multiple, réponse libre.
- **Encoche vide** : Bip salue et réagit au clic, montre les dernières sessions terminées et le bilan du jour ; un clic ailleurs ouvre Claude.
- **Ne pas déranger** pendant 1 h.
- **Installation en une commande** (`installer.cmd`) et désinstallation propre (`desinstaller.cmd`).

### Sécurité

- Échec sûr : toute panne rend la main à Claude, jamais d'autorisation implicite.
- Pipe nommé réservé au compte, serveur vérifié par le hook.
- Boutons armés après 600 ms, commande toujours affichée en entier, caractères invisibles rendus visibles.

### Performances

- ~4,7 % d'un cœur avec une session au travail, ~0,7 % au repos, ~68 Mo de mémoire.

### English summary

Windows notch for Claude Code with Bip, a five-state mascot: live session tracking, permissions and questions answered from the notch, an empty-notch view with recent sessions and today's summary, do-not-disturb, one-command install. Fail-safe by design and light on resources.
