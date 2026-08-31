# Études de cas — l'ingénierie des mods qui durent

*Recherche production CoopAmbitions, 2026-08-31. Source primaire : les repos clonés localement
(`/home/user/smapi`, `/home/user/tmodloader`, `/home/user/nitrox`, `/home/user/jotunn`,
`/home/user/combatextended`, `/home/user/tmpe`, `/home/user/bepinex`, `/home/user/csm`),
complétée par docs, wikis et annonces publiques. Voir [SYNTHESE.md](../SYNTHESE.md) pour le contexte projet.*

---

## Tableau synoptique

| Projet | Jeu | Âge | Rôle | Équipe | Ce qu'on lui vole |
|---|---|---|---|---|---|
| **SMAPI** | Stardew Valley | 2016– | Loader/API | 1 mainteneur + contributeurs | Rewriting de compat, messages d'erreur, écosystème web (log parser, update checks) |
| **tModLoader** | Terraria | 2015– | Loader devenu semi-officiel | 6 « core », vote unanime | Pipeline décompile→diff→patch, cycle de release mensuel, tModPorter |
| **Nitrox** | Subnautica | 2018– | Mod multijoueur | Équipe tournante, Discord-centrée | Organisation : CI, launcher, tests de patchs, Weblate, analyzers maison |
| **Jotunn** | Valheim | 2021– | Bibliothèque d'API | Fusion de 2 libs rivales | Sync de config réseau, handshake de compat des mods, docs générées |
| **Combat Extended** | RimWorld | 2016– | Gros mod gameplay | Fork « -Continued » post-abandon | Survie par ré-organisation, LoadFolders multi-versions, 753 patchs de compat |
| **TM:PE** | Cities: Skylines | 2015– | Mod devenu infrastructure | 4 générations de mainteneurs | Canaux STABLE/TEST, gestion de la dette d'un code hérité |
| **BepInEx** | ~tous jeux Unity | 2018– | Loader générique | Petite équipe | Contrat d'API minimal et gelé (GUID/SemVer/Config), LTS |

---

## 1. SMAPI (Stardew Valley) — le gold standard du loader

### a) Structure du repo et de la solution

`Pathoschild/SMAPI` est une solution .NET exemplairement découpée (`src/SMAPI.slnx`) :

- **`SMAPI`** — le loader lui-même. Le cœur est `Framework/` (60+ sous-systèmes : `ModLoading/`,
  `Content/`, `Logging/`, `Networking/`, `StateTracking/`…) ; la **surface publique pour les mods
  est entièrement faite d'interfaces** à la racine (`IModHelper`, `IMonitor`, `IReflectionHelper`,
  `IMultiplayerHelper`…) — l'implémentation est `internal`. On ne casse pas ce qu'on n'expose pas.
- **`SMAPI.Toolkit` / `Toolkit.CoreInterfaces`** — logique réutilisable (SemVer, manifestes,
  scan de mods) partagée entre le loader, l'installeur et le site web.
- **`SMAPI.ModBuildConfig`** — un **package NuGet officiel pour les moddeurs** : référence les DLL
  du jeu automatiquement, déploie dans le dossier `Mods`, package le zip de release.
- **`SMAPI.ModBuildConfig.Analyzer`** — des **analyzers Roslyn** livrés aux moddeurs
  (`NetFieldAnalyzer`, `ObsoleteFieldAnalyzer`) qui signalent à la compilation les pièges connus
  du jeu (comparer un `NetField` directement, utiliser un champ obsolète).
- **`SMAPI.Web`** — smapi.io : `LogParserController` (l'utilisateur uploade son log, la page
  l'analyse et met en évidence les mods fautifs), `JsonValidatorController`,
  `ModsApiController` (API d'update checks), liste de compatibilité.
- **`SMAPI.Installer`**, `SMAPI.Mods.SaveBackup` (backup automatique des saves, livré en standard),
  `SMAPI.Mods.ConsoleCommands`, et des projets de tests dédiés (dont deux assemblies
  `Tests.ModApiConsumer/Provider` juste pour tester l'API inter-mods).

### b) Comment il encaisse les MAJ du jeu

C'est LA spécialité de SMAPI, à deux niveaux :

1. **Rewriting d'assembly au chargement.** Chaque DLL de mod passe par
   `Framework/ModLoading/AssemblyLoader` (Mono.Cecil) qui applique les handlers déclarés dans
   `Metadata/InstructionMetadata.cs` : des `Finders` (détectent les références cassées et
   produisent un diagnostic précis plutôt qu'un crash) et des `Rewriters`. Le
   `ReplaceReferencesRewriter` remappe champ par champ, méthode par méthode, l'ancienne API vers
   la nouvelle (« ce champ a déménagé de `Farm` vers `GameLocation` »). Pour les changements plus
   profonds, des **façades** : le repo contient 96 classes dans `Rewriters/StardewValley_1_6/`
   (`BootsFacade`, `BuffFacade`…) qui ré-implémentent l'ancienne signature au-dessus de la
   nouvelle API — les références des vieux mods sont réécrites vers la façade. Résultat : une
   énorme fraction des mods compilés pour SDV 1.5 tourne sur 1.6 **sans être recompilée**.
2. **Réaction quasi-immédiate.** SMAPI 4.0 est sorti **le jour même** de Stardew Valley 1.6
   (mars 2024) — Pathoschild a accès aux betas et prépare en amont ; le jeu (ConcernedApe) et lui
   se coordonnent. Les patchs mineurs du jeu sont généralement absorbés en heures/jours, et les
   `release-notes.md` (475 lignes rien que pour les versions récentes) documentent chaque version
   avec des sections séparées « For players » / « For mod authors » / « For the web UI ».

À cela s'ajoutent le **mod blacklist interne** (mods connus pour être cassés/dangereux, refusés au
chargement avec un message explicite) et l'**update check** : chaque manifeste de mod porte des
`UpdateKeys` (`Nexus:2400`, `GitHub:x/y`) ; au lancement, SMAPI interroge `smapi.io/api` qui agrège
Nexus/CurseForge/ModDrop/GitHub **et la liste de compatibilité communautaire** — si le mod installé
est cassé, SMAPI affiche directement le lien vers la version corrigée, y compris les
« unofficial updates » maintenus par la communauté sur le forum.

### c) Outillage maison remarquable

- Le **log parser web** (`smapi.io/log`) : l'utilisateur colle un lien au lieu d'un pavé de texte ;
  le support communautaire diagnostique en secondes. Probablement le meilleur multiplicateur de
  support jamais construit pour un mod.
- Les **analyzers Roslyn distribués aux moddeurs** — la connaissance des pièges du jeu est
  encodée dans l'outillage, pas dans un wiki que personne ne lit.
- `build/` : targets MSBuild qui trouvent le dossier du jeu sur toutes les plateformes
  (`find-game-folder.targets`), custom build de Harmony, script `set-smapi-version.ps1`
  (une commande pour bumper la version partout).
- **Release CI avec attestation GitHub** : build reproductible sur GitHub Actions, attestation
  cryptographique liée au commit (les notes de release y renvoient). Branches `develop` → alpha
  automatiques, merge dans `stable` + tag = release.
- Erreurs pour l'utilisateur : les mods qui échouent sont **groupés par raison** avec des messages
  en langage humain (jusqu'au cas « bloqué par Windows Smart App Control »), en 10+ langues (`i18n/`).

### d) Organisation humaine

Le paradoxe SMAPI : c'est un **projet à mainteneur unique** (Pathoschild) qui a survécu 10 ans —
financé par Patreon, avec des contributions ponctuelles (traductions surtout) créditées dans chaque
note de release. La robustesse ne vient pas du nombre mais de l'**écosystème délégué** : la liste de
compatibilité était éditée par la communauté (wiki, désormais dataset ouvert
`StardewModDataset` + page stats), les unofficial updates sont communautaires, la documentation
moddeur vit sur le wiki officiel du jeu. Le facteur bus reste LE risque connu du projet.

### e) Leçons pour CoopAmbitions

1. **Séparer interface publique et implémentation dès le premier jour** — même pour un mod coop :
   ce que les hooks Harmony touchent du jeu doit passer par une couche d'isolation par domaine
   (déjà prévu dans SYNTHESE §1) ; c'est elle qu'on répare à chaque MAJ de Big Ambitions, pas 50 call sites.
2. **Investir dans le diagnostic utilisateur avant d'avoir des utilisateurs** : un log clair,
   groupé par cause, et un moyen trivial de le partager (bouton « empaqueter les logs », déjà au
   plan §4.9) économisent des centaines d'heures de Discord.
3. **Update check dès la v1** : comparer sa version + le buildid du jeu à un petit JSON hébergé
   (GitHub Pages suffit) et afficher « cette version ne supporte pas la MAJ du 12/09, mettez à
   jour » — le message d'erreur le plus rentable de tout le genre.

---

## 2. tModLoader (Terraria) — le portage industrialisé

### a) Structure du repo et de la solution

`tModLoader/tModLoader` ne contient **pas le code du jeu** mais des **patches** : 547 fichiers
`.patch` sous `patches/` (répartis en 4 cibles : `Terraria`, `TerrariaNetCore`, `tModLoader`,
`ReLogic`). Le contributeur lance l'outil `setup/` qui : décompile le Terraria de son propre
Steam (ILSpy), applique les diffs, et régénère une solution compilable. C'est
l'« open-source sans divulgation » : le code vanilla n'est jamais hébergé.

Autour : `ExampleMod/` (le mod-tutoriel exhaustif, maintenu comme du code de prod),
`tModPorter/`, `tModBuildTasks/`, `tModCodeAssist/` (analyzers pour moddeurs),
`solutions/`, et des fichiers de processus versionnés à la racine : `PortingNotes_1.4.5.md`,
`FailedPatches_1.4.2.txt`, `patching_todo.txt`, `TML_TEAM.md`, `MigrationGuide_1.4.5.md`.

### b) Comment ils encaissent les MAJ de Terraria

Le processus réel, lisible dans le repo :

1. Nouvelle version de Terraria → le setup tool re-décompile, tente de ré-appliquer les 547
   patches ; ceux qui échouent atterrissent dans un rapport (cf. `FailedPatches_1.4.2.txt`).
2. Les mainteneurs les corrigent un par un dans le **PatchReviewer** (GUI maison de review de
   diffs, dans `setup/PatchReviewer/`), et tiennent un **journal de portage public** :
   `PortingNotes_1.4.5.md` est une liste brute de dizaines de questions du type « `NPCSpawnParams.
   strengthMultiplierOverride` renommé en `difficultyOverride` — le comportement a-t-il changé ? ».
   La règle de contribution n°1 est faite pour ça : *appeler le code tML depuis le vanilla, jamais
   l'inverse, et ne jamais refactorer le vanilla* — chaque ligne de diff en moins est du portage en moins.
3. **tModPorter** (168 fichiers C#, avec suite de tests) : un outil **Roslyn qui migre
   automatiquement le code source des mods** — renommages, changements de signatures, hooks
   déplacés — livré aux moddeurs à chaque version majeure.

Temps de réaction réels, assumés publiquement : Terraria 1.4 (mai 2020) → tML 1.4 stable
**2 ans plus tard** ; portage 1.4.4 : **9 mois** ; pour 1.4.5 (2026), l'équipe a annoncé
d'emblée « plusieurs mois » (PC Gamer : *« Updating tModLoader to major Terraria releases is a
huge undertaking »*). Leur réponse au problème n'est pas la vitesse mais la **prévisibilité** :
- **trois lignes maintenues en parallèle** (1.3-legacy, 1.4.3-legacy, 1.4.4+) — les joueurs
  choisissent leur branche Steam et personne ne perd sa collection de mods du jour au lendemain ;
- **cycle mensuel à trois étages** : dev → Preview (gelée, les moddeurs portent) → Stable.
  Les breaking changes sont classés « Runtime Breakage » vs « Source-code Breakage ».

### c) Outillage maison remarquable

Tout leur outillage est la réponse à une même question — *comment porter 547 patches et 10 000
mods à chaque MAJ ?* : setup tool (Decompile/Diff/Patch/HookGen/Format tasks), PatchReviewer,
tModPorter + tests, `tModCodeAssist` (analyzers), CI qui publie sur Steam (nightly/preview/stable).

### d) Organisation humaine

`TML_TEAM.md` est un document de **gouvernance formelle** rare dans le modding : 6 membres nommés
(personnes physiques, avec vrais noms), ajout/retrait de membre **par vote unanime**, membre
injoignable 2 semaines = abstention, propriété de la marque tModLoader distincte de la licence MIT
du code. Financement Patreon partagé à parts égales. Et l'adoubement ultime : Re-Logic distribue
tModLoader **sur Steam comme DLC gratuit officiel** de Terraria (2020) tout en le laissant
communautaire — le studio annonce même ses propres MAJ en coordination avec le calendrier tML.

### e) Leçons pour CoopAmbitions

1. **Documenter le portage pendant qu'on le fait** : un `PortingNotes_<version>.md` versionné,
   même brouillon, transforme la MAJ suivante de « archéologie » en « checklist ». À créer dès la
   première MAJ de Big Ambitions qui nous casse.
2. **La prévisibilité vaut mieux que la vitesse** : annoncer publiquement « la MAJ X nous casse,
   estimation Y, restez sur la version Z du jeu en attendant » (Steam permet de rester sur une
   vieille branche via betas) désamorce 90 % de la pression communautaire.
3. **Minimiser la surface de contact avec le jeu est une politique d'équipe**, pas un détail
   technique : chaque patch Harmony supplémentaire est une dette de portage. Règle de review :
   « ce patch est-il indispensable, et touche-t-il le moins de code possible ? »

---

## 3. Nitrox (Subnautica) — l'organisation d'un mod multijoueur qui a survécu

*(Angle volontairement neuf par rapport à nos recherches passées : pas le netcode, la « boîte ».)*

### a) Structure du repo et de la solution

Solution moderne (`Nitrox.slnx`, .NET 10 pour le launcher) découpée par **rôle de déploiement** :
`NitroxClient` / `NitroxPatcher` (côté jeu), `Nitrox.Server.Subnautica` (serveur autonome),
`Nitrox.Launcher` (application Avalonia multi-plateforme), `Nitrox.Model[.Subnautica]` (partagé),
`Nitrox.Test`, `Nitrox.Assets.Subnautica`. Le build est gouverné par un
`Directory.Build.props` central qui **classe automatiquement chaque projet** (NitroxProject →
NitroxLibrary → UnityModLibrary / TestLibrary) et applique par catégorie : `TreatWarningsAsErrors`,
`InternalsVisibleTo(Nitrox.Test)`, publicizer, analyzers. Central package management
(`Directory.Packages.props`) : une seule source de vérité pour les versions de dépendances.

### b) Comment ils encaissent les MAJ du jeu

- `GameMinimumVersion` (buildid Steam) dans les props de build : le refus de lancer sur une
  version non supportée est un check de première classe, pas un crash.
- **Les tests compilent les patchs contre les vraies DLL du jeu** : `PatchesTranspilerTest`
  applique chaque transpiler Harmony et vérifie le delta d'instructions IL attendu
  (`[typeof(AttackCyclops_OnCollisionEnter_Patch), -17]`…) — une MAJ de Subnautica qui déplace du
  code fait **échouer la CI avant** de faire crasher un joueur.
- **`Reflect.Method((AbusedClass t) => t.Method())`** : toute la réflexion est typée par
  expression lambda au lieu de strings — un renommage dans le jeu devient une erreur de
  compilation, pas une `NullReferenceException` en pleine partie.
- Subnautica met à jour rarement (jeu « fini », puis MAJ Living Large / 2.0) — Nitrox vit donc au
  rythme de ses propres releases, longues (des années entre 1.x majeures, assumé), avec le
  launcher qui gère la bonne association version-mod/version-jeu.

### c) Outillage maison remarquable

- **`Nitrox.Analyzers`** : package NuGet d'analyzers Roslyn maison appliqué à tout le code du
  projet (conventions, pièges Unity).
- **`Nitrox.Discovery.MSBuild`** : package maison qui localise l'installation du jeu à la
  compilation, publié séparément et réutilisable par d'autres mods.
- **Le launcher est le produit** : install/désinstall du mod dans le jeu, gestion des serveurs,
  updates (interroge l'API du site `nitrox.rux`/website API service), news — l'utilisateur ne
  touche jamais un fichier. C'est aussi le paratonnerre à support.
- Publicizer d'assembly standardisé (`BepInEx.AssemblyPublicizer.MSBuild`) plutôt que réflexion.

### d) Organisation humaine

- **CONTRIBUTING.md remarquablement opérationnel** : « contactez-nous sur Discord avant toute
  grosse feature », PRs petites (« one issue per PR »), rebase sur `main`, et depuis 2025-2026 une
  **politique IA explicite** (usage autorisé mais déclaration obligatoire dans la PR, revue humaine
  exigée) — pionnier sur le sujet.
- **Étiquetage de l'entonnoir des contributeurs** : labels `Complexity: easy` (good first issues
  filtrés sans assigné ni PR liée), `Status: to verify` (n'importe qui peut aider en reproduisant
  des bugs). Le tri des issues est conçu comme une rampe d'accès.
- Discord au centre (questions et support y sont redirigés, GitHub réservé aux bugs/features),
  **traductions via Weblate hébergé** (39 langues dans `LanguageFiles/`, contributions de
  non-développeurs), site web avec blog de progrès.
- CI sur runners self-hosted : build 6 runtimes (win/linux/osx × x64/arm64), tests publiés sur
  les PRs, artefacts de launcher téléchargeables par PR (les testeurs Discord testent les PRs
  sans compiler).
- L'équipe a **tourné plusieurs fois** (Sunrunner37 → killzoms → autres) sans mort du projet :
  l'organisation GitHub (`SubnauticaNitrox`), le Discord et le launcher appartiennent au projet,
  pas à une personne.

### e) Leçons pour CoopAmbitions

1. **Écrire les tests qui échouent quand le jeu change** : dès le premier patch Harmony, un test
   qui charge les DLL de Big Ambitions et vérifie que chaque méthode ciblée existe (et, pour les
   transpilers, que l'IL attendu est là). C'est notre système d'alerte avancée pour chaque MAJ/DLC.
2. **La propriété collective des infrastructures** (org GitHub, Discord, canal de distribution)
   est ce qui permet la rotation des mainteneurs. Créer une org GitHub `CoopAmbitions`, pas un
   repo personnel.
3. **Typage > strings partout où on touche le jeu** : un helper `Reflect.Method(...)` façon
   Nitrox et l'interdiction des `AccessTools.Method("Type:Name")` nus dans la review.

---

## 4. Jötunn (Valheim) — la bibliothèque qui synchronise aussi les humains

### a) Structure du repo et de la solution

`JotunnLib/` (la lib), `JotunnDoc/` (mod interne qui **génère la doc depuis le jeu**),
`JotunnBuildTask/`, `TestMod/`, `JotunnTests/`. La lib est organisée en **~20 Managers**
(`PieceManager`, `ItemManager`, `SynchronizationManager`, `NetworkManager`, `ZoneManager`…)
implémentant tous `IManager` — un domaine du jeu = un manager = un point de casse isolé par MAJ.
Publication triple : NuGet (pour compiler), Thunderstore + Nexus (pour jouer), via `publish.ps1`.

### b) Comment ils encaissent les MAJ de Valheim

- Leur promesse marketing est exactement notre problème : *« remove the need to maintain valheim
  version specific code by acting as an interface between the developer and the game's changing
  internals »*. Les centaines de mods Jotunn ne se portent pas — Jotunn se porte.
- La CI **télécharge le Valheim du jour via SteamCMD** (le cache est invalidé par le buildid
  Steam obtenu sur `api.steamcmd.net`) et compile contre — toute divergence avec le jeu réel se
  voit en CI, pas chez les joueurs.
- `GameVersions.cs` + GitVersion : suivi de version du jeu et SemVer strict de la lib.
- Le **MockSystem** (`JVLmock_` : un mod référence un prefab vanilla par nom mocké, résolu au
  runtime) évite d'embarquer des assets du jeu — moins de casse aux MAJ d'assets, et zéro
  problème de copyright.

### c) Outillage maison remarquable — dont LE morceau pour nous

- **`SynchronizationManager`** : toute config BepInEx marquée `IsAdminOnly` est
  **automatiquement synchronisée du serveur vers tous les clients** à la connexion et à chaud
  (events `OnSyncingConfiguration`/`OnConfigurationSynchronized`, RPC dédiés, buffers découpés
  pour les grosses configs, cache des états admin). Un mod n'écrit AUCUN code réseau pour avoir
  une config cohérente entre joueurs.
- **`ModCompatibility` / `NetworkCompatibilityAttribute`** : au handshake de connexion, client et
  serveur échangent la liste `(GUID, version, CompatibilityLevel, VersionStrictness)` de tous les
  mods. `CompatibilityLevel` ∈ {NotEnforced, ServerMustHaveMod, ClientMustHaveMod,
  EveryoneMustHaveMod, VersionCheckOnly} ; `VersionStrictness` ∈ {None, Major, Minor, Patch}.
  Incompatibilité ⇒ connexion refusée **avec une fenêtre listant précisément quoi installer ou
  mettre à jour**. C'est l'état de l'art du « mod checker » prévu dans SYNTHESE §4.9.
- `JotunnDoc` : la doc des données du jeu est générée en lançant le jeu — jamais périmée.
  Docs conceptuelles via DocFX publiées par CI GitHub Pages.
- `JotunnBuildTask` : publicizer d'assembly maison avec hash de la DLL d'origine
  (`JVLOriginalAssemblyHashAttribute`) — rebuild automatique quand le jeu change.

### d) Organisation humaine

Né en 2021 d'une **fusion volontaire de deux bibliothèques concurrentes** (JötunnLib + ValheimLib)
« pour unifier les efforts de la communauté » — décision rare et payante : toute la communauté a
convergé sur une seule API. CONTRIBUTING.md exigeant (Conventional Commits, Git Flow, SemVer,
XML-doc obligatoire sur tout membre public, conventions de nommage des Managers), CHANGELOG
soigné, guides pas-à-pas pour monter l'environnement de dev.

### e) Leçons pour CoopAmbitions

1. **Copier `NetworkCompatibilityAttribute`** quasi tel quel pour notre handshake (SYNTHESE §4.9
   le prévoit ; Jotunn fournit le modèle de données exact : GUID + version + niveau d'exigence +
   strictness, et surtout l'UX du refus : dire QUOI installer).
2. **Sync de config host→invités automatique** : nos options de session (vitesse du temps, règles
   du portefeuille commun…) doivent être un dictionnaire répliqué avec un event de changement —
   pas des packets ad hoc par option.
3. **Un domaine = un manager isolé** : notre découpage par domaines (argent, temps, véhicules,
   employés) doit être structurel (assembly/namespace + interface), pour que chaque MAJ de
   Big Ambitions casse UN fichier identifiable.

---

## 5. Combat Extended (RimWorld) — survivre à la mort de son créateur

### a) Structure du repo

Un repo « contenu d'abord » : `Defs/`, `Patches/` (XML PatchOperations), `Textures/`, `Sounds/`,
`Languages/`, `Source/` (C#), et les dossiers de la matrice de compatibilité : un dossier par DLC
(`Royalty/`, `Ideology/`, `Biotech/`, `Anomaly/`, `Odyssey/`) et **753 dossiers `ModPatches/`**
(un par mod tiers supporté). Le tout piloté par **`LoadFolders.xml`** : RimWorld charge
conditionnellement chaque dossier (`<li IfModActive="sarg.alphaanimals">ModPatches/Alpha
Animals</li>`) — la compat n'est pas du code, c'est de la donnée déclarative.
`Source/` contient aussi des csproj de compat isolés (`MultiplayerCompat` — oui, la compat avec
le mod RimWorld Multiplayer est un projet dédié —, `SOS2Compat`, etc.) et un `Loader` maison.

### b) Comment ils encaissent les MAJ de RimWorld (8 DLC et 6 versions majeures plus tard)

- `About.xml` déclare `<supportedVersions>` ; la structure LoadFolders permet en principe de
  servir plusieurs versions du jeu depuis un seul upload Workshop.
- **Branches `backports-1.4`** avec CI dédiée (`backports.yml`) : les vieux joueurs restent
  servis pendant que `main` suit la dernière version — le workflow reconstruit et publie les
  backports quasi automatiquement.
- Build maison en Python (`Make.py` : csc direct, `-warnaserror`, téléchargement des libs,
  publicizer forké dans l'org) + workflows séparés : style de code, validation **XML du
  LoadFolders** (un analyzer de données !), PR builds, releases.

### c) Outillage maison remarquable

`Make.py`/`BuildCompat.py` (compilation de la matrice de compat), `validate-loadfolders-xml.yml`,
`SupportedThirdPartyMods.md` généré. L'essentiel de leur génie est d'avoir rendu la compatibilité
**contributable par des non-programmeurs** : un patch XML dans un dossier, pas une PR C#.

### d) Organisation humaine — le cœur de l'étude de cas

L'histoire documentée : créé par **NoImageAvailable** (2016) ; en **avril 2020 l'auteur quitte et
archive le repo** (lassitude/conflits communautaires) — mort annoncée du plus gros overhaul de
combat de RimWorld. En **quelques jours**, la communauté crée l'organisation GitHub
**CombatExtended-Continued**, reprend le code (licence CC BY-NC-SA — la licence permissive a
sauvé le mod), et fait tourner depuis une équipe fluide de **150+ contributeurs**, `author: CE Team`.
Six ans plus tard le fork EST le mod (RimWorld 1.6, 16.7.x). Leçon en creux : le projet original
n'a survécu que parce que licence + code public rendaient le fork légitime et facile.

### e) Leçons pour CoopAmbitions

1. **La licence et l'open source sont un plan de succession** : MIT + org GitHub + build
   reproductible par un tiers = le projet peut nous survivre (déjà aligné avec SYNTHESE §4.9,
   à ne jamais négocier).
2. **Rendre la compatibilité déclarative** : nos adaptations par version de Big Ambitions
   (offsets, noms de méthodes patchées si besoin) gagneront à vivre dans des données/dossiers
   conditionnels plutôt qu'en `if (version)` éparpillés.
3. **Warnings-as-errors et validation des données en CI** dès le début — CE valide même son XML ;
   nous devons valider nos définitions de messages réseau (IDs uniques, sérialisation) en CI.

---

## 6. TM:PE (Cities: Skylines) — la dette technique d'un mod devenu infrastructure

### a) Structure du repo

`TLM/` : le mod (`TLM/TLM/` : `Manager/`, `Patch/` — organisé par classe du jeu patchée
(`_PathFind`, `_NetSegment`, `_RoadBaseAI`…), `Custom/` (réimplémentations complètes d'AI du jeu,
héritage pré-Harmony), `Lifecycle/`, `U/` (framework UI maison), `State/`), plus `TMPE.API`
(assembly d'API séparée pour que d'autres mods s'intègrent sans référencer l'implémentation),
`TMPE.UnitTest`, `Benchmarks`, `CSUtil.*`. `docs/` contient des guides de contributeur dont un
`PR_REVIEW_INSTRUCTIONS.md`.

### b) Comment ils encaissent les MAJ du jeu

- **Version gating explicite** : chaque MAJ de C:S déclenche une release « [Meta] Internal
  version check compatible with 1.21.1-f5 » — le mod connaît la version de jeu attendue et
  prévient au lieu de corrompre.
- **Deux entrées Workshop permanentes : STABLE et TEST** — les changements mûrissent chez les
  volontaires avant d'atteindre les dizaines de milliers d'abonnés STABLE. Le CHANGELOG (dates
  au jour près depuis mars 2015, **4 jours après la sortie du jeu**) duplique chaque entrée
  STABLE/TEST.
- Le risque particulier de TM:PE : il **sérialise ses données dans la save** — casser une MAJ
  peut casser des villes de 500 h de jeu. D'où le conservatisme extrême des releases.

### c) Outillage / héritage technique

Le repo est un musée instructif : la couche `Custom/` + `CSUtil.Redirection` (détours pré-Harmony)
coexiste avec `Patch/` (Harmony 2 via CitiesHarmony), `OptionsFramework`, hot-reload des patchs en
dev (`Patch/HotReload`). La migration v11 (2019-2020) — passage à Harmony, découpage en managers,
StyleCop — s'est faite **sans arrêter les releases**, en refactorant par tranches.

### d) Organisation humaine

Quatre générations de mainteneurs documentées : CBeTHaX/SvetlozarValchev (Traffic Manager 2015) →
**Victor-Philipp** (TM:PE, l'auteur du gros du moteur) → **LinuxFan** (qui a fini par manquer de
temps — déménagement, vie réelle : cause d'abandon la plus banale et la plus courante) →
l'organisation **CitiesSkylinesMods** (aubergine18, krzychu124, kian.zarrin…), créée précisément
pour dé-personnaliser la propriété. Le transfert d'une page Workshop étant impossible sans son
propriétaire, chaque succession a coûté une migration d'audience (STABLE 10.20 abandonnée,
nouvelles pages créées) — l'ID Workshop appartient à une personne, pas au projet : piège
structurel de Steam à anticiper.

### e) Leçons pour CoopAmbitions

1. **Publier le Workshop depuis un compte/entité du projet** (ou au minimum documenter la
   co-maintenance Steam) — l'audience Workshop est un actif incessible qu'on ne veut pas perdre
   à chaque succession.
2. **Canal TEST permanent dès qu'on a >100 utilisateurs** : le coût (2 entrées Workshop) est
   nul, la valeur (MAJ du jeu absorbées chez les volontaires d'abord) est énorme — surtout pour
   nous, où un bug touche la save de l'hôte.
3. **Assembly d'API séparée** si d'autres mods veulent interagir avec CoopAmbitions (probable :
   mods de Dudeldups etc.) : ils référencent `CoopAmbitions.API`, jamais l'implémentation.

---

## 7. BepInEx — la stabilité d'API comme produit

### a) Structure et contrat

`BepInEx.Core` + `BepInEx.Preloader.Core` + `Runtimes/` (Unity Mono, Unity IL2CPP, .NET) : un
noyau, des backends. Le contrat exposé aux ~milliers de plugins est **minuscule et gelé** —
`Contract/` contient trois fichiers : `Attributes.cs` (`[BepInPlugin(GUID, Name, Version)]`,
`[BepInDependency]` avec flags Hard/Soft et plages de versions, `[BepInProcess]`),
`IPlugin`/`PluginInfo`, plus le sous-système `Configuration/` (ConfigFile TOML typé,
`AcceptableValueRange`, event `SettingChanged` — devenu le standard de facto que Jotunn
synchronise en réseau). Le commentaire du constructeur dit tout : *« GUID: should not change
between plugin versions »* — l'identité est éternelle, le reste est SemVer.

### b) Gestion de la stabilité

BepInEx 5 (2019) est resté l'API stable pendant que la v6 (IL2CPP, .NET moderne) a passé **6+ ans
en bleeding edge** (builds numérotés be.XXX, breaking changes concentrés avant la pre-release) —
choix assumé : une branche **v5-lts** maintenue séparément, car des écosystèmes entiers
(Valheim, Lethal Company…) reposent sur la v5. La leçon : quand des tiers dépendent de vous,
**on ne ship pas une v majeure tant qu'elle n'est pas finie, et on maintient l'ancienne**.
Distribution des libs via NuGet + feed BepInEx propre, docs DocFX versionnées (docs.bepinex.dev).

### c) Leçons pour CoopAmbitions

1. **Un identifiant stable + SemVer + dépendances déclarées** : notre protocole réseau doit avoir
   son propre numéro de version, indépendant de la version du mod (le handshake compare le
   protocole, le mod checker compare les mods).
2. **Le fichier de config typé avec plages de valeurs et event de changement** est un pattern à
   copier (et il se marie avec la leçon Jotunn : sync réseau des configs).

---

## Synthèse transversale

### Ce que font TOUS les mods qui durent (les invariants)

1. **Un tampon entre le jeu et tout le reste.** Rewriters + façades (SMAPI), managers par domaine
   (Jotunn), patches minimaux et localisés (tModLoader), couche API séparée (TM:PE). À chaque MAJ
   du jeu, on répare LE tampon, jamais l'écosystème. *Chez nous : la couche d'accès par domaine de
   SYNTHESE §1 est ce tampon — la traiter comme le composant le plus précieux du code.*
2. **La MAJ du jeu est un événement planifié, pas une catastrophe.** Version/buildid attendu
   vérifié au boot avec message clair (TM:PE, Nitrox, SDK Big Ambitions déjà), CI qui compile
   contre le jeu du jour (Jotunn via SteamCMD), tests qui cassent quand le jeu bouge (Nitrox),
   notes de portage versionnées (tModLoader), communication publique du délai (tous).
3. **Deux canaux de release minimum** (stable + test/preview/nightly) : tModLoader, TM:PE,
   Nitrox (artefacts par PR), SMAPI (alphas de develop). Personne ne teste sur ses vrais joueurs.
4. **Versionnage sémantique + handshake + refus explicite.** Tous ont un mécanisme qui empêche
   des versions incompatibles de se parler ou de se charger, avec un message qui dit quoi faire
   (Jotunn est le modèle abouti côté réseau).
5. **Le diagnostic utilisateur est un produit** : log parser web SMAPI, launcher Nitrox,
   fenêtres d'erreur actionnables. Les projets qui durent ont industrialisé leur support —
   c'est ce qui protège les mainteneurs du burn-out par Discord.
6. **La connaissance est encodée dans l'outillage** : analyzers Roslyn (SMAPI, Nitrox,
   tModCodeAssist), migrateurs automatiques (tModPorter), validateurs de données (CE), doc
   générée (Jotunn, BepInEx). Le wiki s'oublie, l'analyzer s'exécute.
7. **La propriété est collective et transférable** : org GitHub (pas un compte perso), licence
   permissive, build reproductible par un inconnu, gouvernance écrite (TML_TEAM.md est le modèle),
   Discord/site appartenant au projet. C'est l'assurance-vie : CE et TM:PE n'existent aujourd'hui
   que grâce à ça.
8. **Une rampe d'accès pour contributeurs** : good first issues étiquetées, CONTRIBUTING
   opérationnel (« venez sur Discord avant une grosse feature »), petites PRs exigées,
   contributions non-code possibles (traductions Weblate, patchs XML, vérification de bugs) —
   et du crédit visible partout (release notes SMAPI, contrib.rocks tML).

### Les pièges qui tuent les mods (causes d'abandon documentées)

1. **Le facteur bus non couvert.** CE archivé du jour au lendemain par son unique auteur (2020) ;
   TM:PE orphelin quand LinuxFan a déménagé ; d'innombrables mods SDV listés « broken » en
   attente d'updates non-officielles. Antidote : org + licence + n°7 ci-dessus. (SMAPI est
   l'exception qui survit en solo — grâce à un écosystème délégué et un financement.)
2. **Le couplage diffus au jeu.** Les mods dont les accès au jeu sont éparpillés meurent à la
   première grosse MAJ : le coût de portage dépasse la motivation. C'est la cause n°1 des
   cimetières de Workshop après chaque « update 1.x » d'un jeu. (Même tModLoader, outillé à
   l'extrême, paie 9-24 mois par version majeure de Terraria.)
3. **La page Workshop personnelle.** L'audience Steam n'est pas transférable : chaque succession
   TM:PE a dû abandonner des dizaines de milliers d'abonnés sur une page morte. Pour nous
   s'ajoute le précédent Going Public (retrait probable du Workshop) : distribution multi-canal
   obligatoire (Workshop + GitHub Releases), comme déjà acté en SYNTHESE §4.9.
4. **Le closed source comme fossoyeur.** Going Public : closed source, un auteur, disparition =
   tout le savoir perdu (notre propre rapport art-antérieur). Contre-exemple : CE, forké et
   vivant 6 ans après son abandon.
5. **Le burn-out par support et par communauté toxique.** Cause récurrente et documentée
   (départ de NoImageAvailable, témoignages de moddeurs) — les invariants 5 et 8 sont les
   défenses : outiller le support, partager la charge, et un code of conduct appliqué (Nitrox).
6. **La v2 éternelle.** BepInEx 6 (6+ ans de bleeding edge) et tModLoader 1.4 (2 ans) n'ont pas
   tué leurs projets uniquement parce que la version stable précédente restait maintenue et que
   la communication était transparente. Un petit projet qui s'arrête de release pour « tout
   refaire » meurt. *Chez nous : jamais de réécriture qui bloque les releases ; refactorer par
   tranches comme TM:PE v11.*
7. **Perdre l'intérêt pour le jeu lui-même** (Starfield Together, abandonné car « le jeu est
   ennuyeux ») — non-outillable, mais une raison de plus de garder le mod petit, modulaire et
   forkable : si nous partons, d'autres doivent pouvoir continuer.

### Application immédiate à CoopAmbitions (ordre de priorité)

1. Org GitHub + MIT + CI qui build sans secrets locaux (invariants 7) — coût quasi nul maintenant,
   impossible à rattraper plus tard.
2. Handshake façon Jotunn (protocole versionné + liste de mods + strictness + message actionnable)
   — déjà partiellement codé, finir avec le modèle `NetworkCompatibilityAttribute`.
3. Test « le jeu a bougé » façon Nitrox sur chaque méthode patchée/accédée, branché sur le buildid
   Steam de Big Ambitions — notre alarme MAJ/DLC.
4. `PortingNotes_<version>.md` dès la première MAJ qui casse + annonce publique du délai
   (culture tModLoader).
5. Canal TEST (Workshop/GitHub pre-release) avant d'atteindre une vraie audience ; bouton
   « empaqueter les logs » dans la v1.

---

## Sources

**Code (clones locaux, source primaire)** : `/home/user/smapi` (Pathoschild/SMAPI),
`/home/user/tmodloader` (tModLoader/tModLoader), `/home/user/nitrox` (SubnauticaNitrox/Nitrox),
`/home/user/jotunn` (Valheim-Modding/Jotunn), `/home/user/combatextended`
(CombatExtended-Continued/CombatExtended), `/home/user/tmpe` (CitiesSkylinesMods/TMPE),
`/home/user/bepinex` (BepInEx/BepInEx), `/home/user/csm` (CitiesSkylinesMultiplayer/CSM).

**Web** :
[SMAPI release notes & attestations](https://github.com/Pathoschild/SMAPI/blob/develop/docs/release-notes.md) ·
[SmapiCompatibilityList](https://github.com/Pathoschild/SmapiCompatibilityList) ·
[Migrate to SDV 1.6](https://stardewvalleywiki.com/Modding:Migrate_to_Stardew_Valley_1.6) ·
[Unofficial mod updates (forum)](https://forums.stardewvalley.net/threads/unofficial-mod-updates.2096/) ·
[tModLoader development pipeline](https://github.com/tModLoader/tModLoader/wiki/The-tModLoader-development-pipeline) ·
[tModLoader Release Cycle](https://github.com/tModLoader/tModLoader/wiki/tModLoader-Release-Cycle) ·
[tModLoader 1.4.5 status (issue #5070)](https://github.com/tModLoader/tModLoader/issues/5070) ·
[PC Gamer — « a huge undertaking »](https://www.pcgamer.com/games/survival-crafting/terrarias-biggest-mod-manager-will-require-months-before-its-fully-compatible-with-the-1-4-5-patch-updating-tmodloader-to-major-terraria-releases-is-a-huge-undertaking/) ·
[tModLoader sur Steam (Re-Logic)](https://store.steampowered.com/app/1281930/tModLoader/) ·
[Nitrox releases](https://github.com/SubnauticaNitrox/Nitrox/releases) ·
[Weblate Nitrox](https://hosted.weblate.org/engage/subnauticanitrox/) ·
[Jotunn — docs](https://valheim-modding.github.io/Jotunn/) ·
[Jotunn Thunderstore (histoire de la fusion)](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/) ·
[CombatExtended-Continued (org)](https://github.com/CombatExtended-Continued) ·
[TM:PE CHANGELOG (historique 2015→)](https://github.com/CitiesSkylinesMods/TMPE/blob/master/CHANGELOG.md) ·
[TM:PE Workshop STABLE](https://steamcommunity.com/sharedfiles/filedetails/?id=1637663252) ·
[BepInEx releases / v5-lts](https://github.com/BepInEx/BepInEx/releases) ·
[BepInEx 6.0.0-pre.2 (discussion #969)](https://github.com/BepInEx/BepInEx/discussions/969) ·
[Starfield Together abandonné](https://comicbook.com/gaming/news/starfield-together-multiplayer-mod-canceled/).
