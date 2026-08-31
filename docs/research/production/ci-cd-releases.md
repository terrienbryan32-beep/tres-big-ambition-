# CI/CD, releases et gestion des MAJ du jeu — comment font les mods sérieux, et ce qui s'applique à nous

*Recherche du 2026-08-31. Sources : lecture directe des workflows GitHub Actions de
Nitrox (Subnautica), SMAPI (Stardew Valley), Jotunn (Valheim), CSM (Cities: Skylines),
tModLoader (Terraria), CombatExtended (RimWorld), plus le SDK officiel Big Ambitions et
l'outillage de build externe de Dudeldups (clones locaux `/home/user/*`), et recherche web
(GameLib Dehumidifier, Krafs.Rimworld.Ref, tcli, game-ci, steamcmd).*

**Notre contrainte, en une phrase** : CoopAmbitions se compile via le SDK officiel dans
l'éditeur Unity 2022.3.62f2, contre ~35 DLL du jeu importées depuis l'installation Steam
(`Assets/_BaDependencies/GameDlls/`) — DLL propriétaires, non commitables, absentes d'un
runner CI ; la distribution visée est Steam Workshop (upload in-game) + GitHub Releases.

---

## 1. CI sans les DLL du jeu — les solutions observées dans la vraie vie

C'est LE problème de tout mod de jeu propriétaire, et chaque grosse communauté l'a résolu
différemment. Panorama complet, du plus public au plus privé :

### 1.a Packages NuGet publics d'assemblies de référence (« GameLibs »)

Une *reference assembly* est une DLL dont tout le code (les corps de méthodes) a été
retiré : il ne reste que les signatures — exactement ce dont le compilateur a besoin.
Trois communautés majeures publient ça publiquement :

| Communauté | Package/repo | Qui le produit | Outil | Position légale affichée |
|---|---|---|---|---|
| RimWorld | [`Krafs.Rimworld.Ref`](https://www.nuget.org/packages/Krafs.Rimworld.Ref) (nuget.org) | krafs ([RimRef](https://github.com/krafs/RimRef)) | strip des corps de méthodes | **Publié avec la permission explicite de Ludeon Studios** — le précédent « propre » |
| Stardew Valley | [`StardewModders/mod-reference-assemblies`](https://github.com/StardewModders/mod-reference-assemblies) (repo GitHub public, cloné par le CI de SMAPI) | l'orga StardewModders | **Refasmer** (JetBrains) | rien d'explicite dans le README ; toléré de longue date, ConcernedApe est mod-friendly |
| Lethal Company | [`LethalCompany.GameLibs.Steam`](https://www.nuget.org/packages/LethalCompany.GameLibs.Steam) (nuget.org + nuget.bepinex.dev) | Lordfirespeed via [NuGet-GameLib-Dehumidifier](https://github.com/Lordfirespeed/NuGet-GameLib-Dehumidifier) | strip **et** publicize automatisés | argumentaire « stubbed assemblies = pas de redistribution de propriété intellectuelle » |

Le pipeline **GameLib Dehumidifier** (Lethal Company) est le plus industrialisé :
un workflow nightly (`checkAllGamesForUpdates`) interroge les infos Steam de chaque jeu
suivi ; à la détection d'une nouvelle version, un second workflow ouvre une PR pour saisir
le numéro de version ; un troisième télécharge le depot, strip/publicize les assemblies,
construit le package et le pousse sur NuGet.org. Versionnage calqué sur celui du jeu
(ex. `73.0.0-ngd.0` = version 73 du jeu).

**Position légale, en pratique** : le consensus communautaire (documenté notamment dans le
[wiki Risk of Rain 2](https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Assembly-References/)
et le guide de ghorsington/BepInEx) distingue nettement : DLL strippées (métadonnées
seules) = distribuables « for interoperability », DLL avec corps de méthodes = **jamais**
(« don't upload that dll on github or somewhere else »). Ce n'est pas un avis d'avocat :
une reference assembly reste une œuvre dérivée, et seul l'accord du studio (cas RimRef)
rend la chose incontestable. Aucun cas connu de takedown contre des GameLibs strippées,
mais les précédents existent uniquement là où le studio est bienveillant.

**Pour nous** : Hovgaard Games publie son SDK sous MIT sur GitHub et pousse activement le
modding — c'est le profil exact de studio à qui **demander la permission** (comme krafs
l'a fait avec Ludeon). Un message au Discord/forum Hovgaard demandant l'autorisation de
publier des reference assemblies strippées (ou mieux : suggérer qu'ILS les publient dans
le repo du SDK, ce qui réglerait le problème pour toute la communauté BA) est l'étape 1.
En attendant la réponse : option privée ci-dessous.

### 1.b Repo privé / submodule de DLL strippées (NStrip/Refasmer)

Le pattern intermédiaire : on strip soi-même, on stocke en **privé**, le CI y accède.

- Outils : [NStrip](https://github.com/bbepis/NStrip) (BepInEx) — mode strip-only qui
  vide les corps de méthodes (option publicize `-p` en bonus, inutile pour nous tant
  qu'on ne référence pas de membres privés) ; ou **Refasmer** de JetBrains
  (`dotnet tool install -g JetBrains.Refasmer.CliTool` puis `refasmer -O refs -c *.dll`),
  choisi par StardewModders, plus « canonique » (produit de vraies ReferenceAssemblies
  avec l'attribut adéquat).
- Stockage observé :
  - **CSM** (multijoueur Cities: Skylines) : le CI télécharge un
    `https://storage.citiesskylinesmultiplayer.com/Assemblies.zip` hébergé par l'équipe —
    simple, mais le zip est public (position plus fragile que le strip).
  - **SMAPI** : repo GitHub public dédié (voir 1.a).
  - Variante courante : **submodule Git privé** (le CI a un deploy key) ou **GitHub
    Release privée** téléchargée avec le `GITHUB_TOKEN`.
- Position légale : identique au 1.a moins l'exposition publique — un repo privé entre
  mainteneurs est de facto ce que tout le monde fait déjà en s'échangeant un dossier
  `Managed/`. Risque pratique quasi nul ; à convertir en 1.a si Hovgaard dit oui.

### 1.c Secrets/artefacts de CI

Pousser un zip chiffré des DLL en secret GitHub (limite 48 Ko/secret → impraticable) ou
en artefact/cache : personne de sérieux ne fait ça en dur. La variante réaliste est le
**cache keyed par buildid** : Jotunn (Valheim) met en cache l'installation du jeu avec
pour clé le buildid Steam courant, réinstallant seulement quand le jeu change. Mais leur
source de vérité est réinstallable anonymement (voir 1.d) — un cache doit pouvoir être
reconstruit, donc ce n'est pas une solution de stockage primaire pour nous.

### 1.d Téléchargement du jeu dans le CI via Steam

- **Jotunn** : `steamcmd +login anonymous +app_update 896660` — le **serveur dédié**
  Valheim est un app gratuit et anonyme. C'est la solution idéale… pour les jeux qui ont
  un serveur dédié. **Big Ambitions n'en a pas** → inapplicable.
- **GameLib Dehumidifier / DepotDownloader** : téléchargement du depot du jeu avec un
  **compte possédant le jeu**, credentials en secrets CI. Légalité : le Steam Subscriber
  Agreement interdit le partage de compte ; mettre ses credentials dans GitHub Actions
  est un usage « gris » toléré (largement pratiqué, dont tModLoader pour son propre
  déploiement) mais expose le compte à un flag/Steam Guard et les credentials à toute
  personne ayant accès aux secrets du repo. Si un jour on le fait : **compte dédié**
  n'ayant que BA, jamais le compte perso.
- **Nitrox** : contourne tout ça avec des **runners self-hosted** où le jeu est installé
  (`runs-on: [self-hosted, linux]`, chemin du jeu résolu par MSBuild via
  `SUBNAUTICA_INSTALLATION_PATH`). Robuste mais coût d'infra : une machine à maintenir,
  et pour un petit projet c'est disproportionné.

### 1.e CI limitée : tout ce qui ne touche pas le jeu

Le plancher, toujours disponible sans aucune DLL : lint/format (`dotnet format`,
`.editorconfig`), analyse statique, et **tests unitaires de la logique pure**. Notre
protocole `NetMessage` (octet de type + payload BinaryWriter) est 100 % testable ainsi —
voir §6.

### Évaluation pour CoopAmbitions

| Option | Faisable ? | Effort | Verdict |
|---|---|---|---|
| 1.a NuGet public | après accord Hovgaard | moyen | **cible long terme** — à demander tôt |
| 1.b Submodule/repo privé de refs strippées (Refasmer) | oui, tout de suite | faible (script one-shot + re-run à chaque MAJ) | **recommandé v1** |
| 1.c Secrets/artefacts seuls | non (taille, reconstruction) | — | non |
| 1.d steamcmd anonyme | non (pas de serveur dédié BA) | — | non |
| 1.d DepotDownloader + compte | oui mais ToS gris | moyen | seulement si on automatise la détection de MAJ à fond (§4), compte dédié |
| 1.d Runner self-hosted | oui | élevé | overkill à notre échelle |
| 1.e Lint + tests purs | oui, aujourd'hui | quasi nul | **v0 immédiat** |

Point capital découvert dans l'outillage de **Dudeldups** (le moddeur BA le plus
prolifique, ~15 mods) : son repo contient `tools/external-build/BuildBigAmbitionsMods.ps1`
qui compile les mods BA **sans le Mod Builder Unity** — génération d'un csproj `net472`
avec `EnableDefaultCompileItems=false`, sources du dossier du mod, références =
`Assets/_BaDependencies/GameDlls/*.dll` + les `UnityEngine*.dll` du dossier de l'éditeur,
defines `BA_GAME_DLLS_IMPORTED;UNITY_2022_3;UNITY_STANDALONE_WIN;NET_4_8…`, puis un
`dotnet build` ordinaire. **La compilation d'un mod BA est donc un problème .NET
classique** : remplacez les GameDlls par leurs reference assemblies strippées et les
UnityEngine par le package NuGet `UnityEngine.Modules` (version 2022.3.x), et le même
build tourne sur `ubuntu-latest`. L'éditeur Unity n'est réellement requis que pour :
le `ModManifest.asset` (ScriptableObject — mais c'est du YAML texte, stable une fois
créé), les asset bundles (on n'en a pas), et le packaging/install officiel.

---

## 2. Builds Unity headless en CI — réaliste ou overkill ?

Ce qui existe :

- **[game-ci/unity-builder](https://game.ci/docs/github/builder/)** : images Docker de
  l'éditeur Unity (plusieurs Go) + activation de licence automatisée. Licences :
  Unity a **supprimé l'activation manuelle des licences Personal** (plus de `.ulf` via la
  page webactivation) ; game-ci v4 active désormais une Personal license avec
  `UNITY_EMAIL`/`UNITY_PASSWORD` (+ gestion 2FA pénible — code par email, d'où des
  actions dédiées comme `buildalon/activate-unity-license`), ou licence
  Pro/Floating pour les pros. Ça marche, mais c'est le morceau le plus fragile de tout
  pipeline game-ci (les threads « License expired » sont un genre littéraire à part).
- Temps/coût typiques : premier build 15-40 min (pull de l'image + import du projet),
  ensuite 5-15 min avec un cache du dossier `Library/` (le cache Library pèse vite
  plusieurs Go et sature le quota de cache GitHub de 10 Go).
- Le SDK BA compile les mods via `AssemblyBuilder` de l'éditeur
  (`Assets/Editor/ModBuilder/ModPackager.cs`) — techniquement pilotable en
  `-batchmode -executeMethod` avec une méthode statique à nous qui appelle
  `ModPackager.EnqueueAll`.

**Verdict pour nous : overkill, et doublement bloqué.** Même la licence résolue, le
projet SDK ne s'ouvre pas sans les DLL du jeu (l'import est le préalable du
WelcomeWindow) — il faudrait donc de toute façon résoudre le §1, et à ce stade le
`dotnet build` façon Dudeldups produit la même DLL pour un coût CI de ~1 minute.
Unity headless ne redeviendrait pertinent que si le mod embarque un jour des **asset
bundles** (avatars custom, UI prefabs) : ce jour-là, réévaluer game-ci OU simplement
builder les bundles à la main dans l'éditeur (ils changent rarement) et ne mettre en CI
que le code.

---

## 3. Releases : ce que font les gros mods

### Versioning et changelogs observés

- **Semantic versioning** partout, avec deux écoles : semver pur piloté par tag
  (`v2.3.0` → SMAPI vérifie même que le tag == la version dans les sources et échoue
  sinon — bonne pratique), ou **GitVersion** calculé depuis l'historique (Jotunn :
  `major.minor.patch-commitsSinceVersionSource`). tModLoader et CSM utilisent des
  versions calendaire (`2026.08.x`, `YYMM.run_number`) — adapté à leur cadence, pas à la
  nôtre.
- **Changelogs** : Keep a Changelog tenu à la main reste majoritaire chez les mods
  (le public lit le changelog sur la page Workshop/Thunderstore, pas sur GitHub).
  Les générateurs ([release-please](https://github.com/googleapis/release-please) — PR de
  release automatique depuis des commits conventionnels ; [git-cliff](https://git-cliff.org)
  — génération pure depuis les commits, sans opinion sur le workflow) sont surreprésentés
  chez les libs, sous-représentés chez les mods. git-cliff est le meilleur ratio
  bénéfice/contrainte pour nous : il n'impose rien au repo, il formate juste.
- Détail BA : le `ModManifest.asset` a un champ `Version:` en YAML texte → un
  `sed -i 's/^  Version: .*/  Version: X.Y.Z/'` en CI suffit à le synchroniser (Jotunn
  fait exactement ça sur son `manifest.json` Thunderstore + `Main.cs`, et commite le
  bump depuis le workflow).

### Packaging et publication multi-canal

- **GitHub Releases** : standard absolu, via `softprops/action-gh-release` (Jotunn) sur
  push de tag ; SMAPI ajoute les **attestations d'artefacts** (`actions/attest`) — surcoût
  nul, valeur réelle pour un mod réseau (prouver que le zip vient bien du CI du repo).
- **Thunderstore** : [tcli](https://github.com/thunderstore-io/thunderstore-cli)
  (`thunderstore.toml` + `tcli publish --token`) ou l'action
  [`GreenTF/upload-thunderstore-package`](https://github.com/marketplace/actions/upload-thunderstore-package).
  Zip = `manifest.json` (name, version_number, dependencies) + `README.md` + `icon.png`.
  **Non pertinent pour BA** (pas de communauté Thunderstore) mais le format « zip +
  manifest + icône » est le modèle de notre zip GitHub Releases.
- **Nexus** : automatisable via `unex`/NexusUploader (clé API + cookies)… que Jotunn a
  **désactivé en le commentant** dans son workflow — l'API Nexus est le canal le plus
  fragile. À ne faire qu'à la main, si jamais.
- **Steam Workshop hors du jeu** : oui, c'est automatisable en général —
  `steamcmd +login <user> +workshop_build_item item.vdf` avec un VDF
  (`appid`, `publishedfileid`, `contentfolder`, `changenote`) ; des actions clé en main
  existent ([Weilbyte/steam-workshop-upload](https://github.com/Weilbyte/steam-workshop-upload),
  [gmod-workshop/workshop-upload](https://github.com/gmod-workshop/workshop-upload),
  [isaac-steam-workshop-upload](https://github.com/IsaacScript/isaac-steam-workshop-upload)).
  L'authentification se fait en exportant le `config.vdf` d'une session steamcmd locale
  en secret base64 (pattern utilisé en production par tModLoader pour `run_app_build_http`,
  avec dans leur workflow le commentaire savoureux : « si ça échoue avec License expired,
  STEAM_CONFIG_VDF doit être rafraîchi »).

  **MAIS pour Big Ambitions, précautions** : l'upload officiel passe par le Mod Creator
  **in-game** (`Mods > Mod Creator`, thumbnail < 1 Mo à la racine), qui pose
  vraisemblablement ses propres métadonnées/tags UGC que le jeu attend au chargement.
  Recommandation : **création initiale de l'item toujours in-game** ; tester ensuite (une
  fois, sur un item de test) si `workshop_build_item` avec le `publishedfileid` existant
  met à jour le contenu sans casser la fiche. Si oui → mise à jour Workshop
  automatisable ; sinon → le Workshop reste manuel (5 min par release) et seul GitHub
  Releases est automatisé. Ne pas parier le pipeline dessus tant que ce test n'est pas
  fait.

### Le geste qui sauve : l'ordre de publication

Pattern observé chez tous les multi-canaux : GitHub Release d'abord (c'est l'artefact de
référence + le changelog), Workshop ensuite, annonce Discord en dernier. En cas de pépin
Workshop, le lien GitHub est déjà dans l'annonce.

---

## 4. Gestion des mises à jour du jeu

Chaque MAJ Steam de BA invalide potentiellement : les DLL importées (le SDK trace le
`buildid` — `GameDllImporter` compare le buildid importé au buildid courant lu dans
`steamapps/appmanifest_1331550.acf` et affiche « game updated — re-import »), nos
patches Harmony, et nos heuristiques d'accès à l'état du jeu.

### Patterns observés

1. **Fichier/métadonnée de compatibilité versions jeu ↔ versions mod**
   - SMAPI : `manifest.json` de chaque mod avec `MinimumApiVersion` + `UpdateKeys`,
     croisé avec une **liste de compatibilité communautaire** (wiki → smapi.io/mods) que
     SMAPI consulte pour **désactiver les mods cassés avec un message clair** — l'état de
     l'art absolu, mais il suppose un loader central que BA n'a pas (le loader BA est le
     jeu lui-même).
   - RimWorld : `About.xml` avec `<supportedVersions>` → le jeu affiche un warning
     natif. Le manifest BA n'a **pas** de champ équivalent (ModId, DisplayName, Author,
     Version, assembly, locales, bundles, plateformes — c'est tout) → c'est à nous de
     porter cette info.
   - Nitrox : `GameMinimumVersion` (un buildid : `82304`) dans `Directory.Build.props`,
     vérifié au build ET au runtime.
2. **Branches par version du jeu** : CombatExtended maintient des branches
   `Rimworld-Version-1.4` etc., buildées par le même workflow. Pertinent quand le jeu
   garde des branches Steam legacy longtemps ; pour BA post-1.0, une seule branche
   `main` + tags suffit tant que Hovgaard ne maintient pas de vieille version.
3. **Détection au runtime + message clair** : les bons mods échouent *bruyamment et
   poliment* : au chargement, comparer la version du jeu (buildid lu dans l'ACF via la
   même technique que `SteamInstallLocator`, ou `Application.version`) à la plage testée ;
   si inconnue → bannière in-game « Le jeu a été mis à jour, CoopAmbitions n'a pas encore
   été validé pour cette version — l'hébergement est désactivé, suivez [lien] », plutôt
   qu'une NullReferenceException dans un patch. Notre handshake réseau versionné
   (Hello/Welcome) doit transporter **la version du mod ET le buildid du jeu** : refuser
   la connexion si les buildids diffèrent évite les desyncs inexplicables entre un hôte à
   jour et un client pas encore patché par Steam.
4. **Communication « game updated, hold on »** : le rituel observé partout (Valheim à
   chaque patch, Lethal Company v50…) : épingler immédiatement un message
   (page Workshop/Discord/README) « la MAJ du jour casse le mod, ne nous spammez pas, fix
   en cours », puis release corrective. Le silence est ce qui tue la confiance — pas le
   délai. Prévoir un template de message prêt à coller.
5. **Pipelines de rebuild automatique à la détection d'un buildid** :
   - Le plus léger, observé chez Jotunn : interroger `https://api.steamcmd.net/v1/info/<appid>`
     (API tierce au-dessus de steamcmd) et lire `.data."<appid>".depots.branches.public.buildid`.
     Un workflow `schedule:` quotidien compare au buildid connu (commité dans un fichier),
     et **ouvre une issue** (ou poste sur Discord) quand ça bouge. SteamDB n'offre pas de
     webhooks publics ; ce polling est le standard de fait.
   - Le maximaliste, GameLib Dehumidifier : détection → DepotDownloader → re-strip →
     nouvelle version du package de refs → PR. Réservé au jour où on a l'accord Hovgaard
     (public) ou un compte Steam dédié (ToS gris, cf. §1.d) ; d'ici là, la MAJ des refs
     strippées reste un geste manuel de 2 minutes (re-import SDK, re-run du script
     Refasmer, push du submodule privé).
   - **On ne peut de toute façon pas « auto-fixer »** : un nouveau buildid demande un
     humain qui relance le jeu, re-teste le MVP à deux comptes et rejoue la session dnSpy
     si un patch cible a bougé. L'automatisation utile est la **notification**, pas le
     rebuild.

---

## 5. Qualité avant release

- **Checklists** : les gros mods (SMAPI en tête) font des releases *procédurales* :
  version bumpée partout (chez nous : `ModManifest.asset`, constante
  `CoopMod.Version`, version du protocole réseau si le format des messages a changé),
  changelog à jour, test de fumée in-game. Notre checklist minimale spécifique coop :
  **matrice de smoke test à 2 comptes** — héberger/rejoindre, re-rejoindre après crash
  de l'invité, MAJ dispo côté client pas encore appliquée (le handshake doit refuser
  proprement), sauvegarde/rechargement de la partie hôte.
- **Canaux beta** :
  - Le Workshop n'a **pas de canal beta par item**. Deux patterns observés : un
    **second item Workshop « [Beta] »** (l'approche la plus courante) ou distribution
    beta hors-Workshop. Pour BA, notre dossier `ModsLocal` est parfait pour ça : une
    **GitHub pre-release** (`v0.3.0-beta.1`, cochée « pre-release ») avec le zip à
    dézipper dans `ModsLocal/` — les testeurs n'ont pas besoin du Workshop du tout.
  - tModLoader pousse ses branches `preview`/`stable` vers des branches beta **de
    l'app Steam** — possible seulement pour le propriétaire de l'app, pas pour un mod.
- **Programmes de testeurs** : universellement un canal Discord privé + pre-releases
  GitHub. Le coop ajoute une contrainte : il faut des testeurs *par paires* — planifier
  des sessions plutôt qu'espérer du test spontané.
- **Télémétrie de crash** : Sentry a un SDK Unity qui marcherait techniquement dans un
  mod, et le consentement préalable est supporté. Mais la communauté modding est
  **notoirement hostile** à toute télémétrie (mods « No Telemetry » populaires sur
  Minecraft, demandes de labels de divulgation sur Modrinth, backlashes réguliers sur
  Steam) ; pour un mod *réseau*, déjà suspect par nature, embarquer un client Sentry est
  un risque réputationnel net. Notre SYNTHESE tranche déjà : **pas de télémétrie** ;
  l'alternative observée qui marche : un **bouton « signaler un problème » qui empaquette
  les logs localement** et ouvre une issue GitHub pré-remplie — l'utilisateur voit et
  envoie lui-même ce qui part. SMAPI fait pareil avec son log parser web
  (l'utilisateur uploade son log volontairement).

---

## 6. Tests — comment les mods testent sans le jeu

### Ce qui est observé

- **Nitrox** est le modèle exact pour nous (mod multijoueur, protocole binaire) :
  - `Nitrox.Test/Model/Packets/PacketsSerializableTest.cs` : énumère **par réflexion
    tous les types de Packet** de l'assembly, génère des instances aléatoires via un
    Faker (Bogus), sérialise → désérialise → deep-compare (CompareNETObjects). Aucun
    packet ne peut être ajouté sans être couvert. À copier tel quel pour `NetMessage`.
  - `PacketSuppressorTest.cs` : le flag anti-boucle-d'écho (notre PacketSuppressor du
    jour 1) est testé unitairement.
  - Toute la machine à états de connexion (`ConnectionState/*Tests`) est testée avec des
    fakes — la partie la plus faillible d'un mod réseau est justement testable sans jeu.
- **SMAPI** : suite NUnit sur le Toolkit (parsing semver, manifests, résolution de
  dépendances) — la logique « meta » est testée, le couplage au jeu ne l'est pas.
- **tModLoader** : compile `ExampleMod` dans le CI comme test d'intégration de l'API —
  l'équivalent pour nous : le build CI du mod lui-même contre les refs strippées (§1)
  est déjà un test de non-régression de nos usages de l'API BA.
- **Fakes/interfaces au-dessus de l'API du jeu** : pattern Nitrox — le code de gameplay
  parle à des interfaces (`IGameClock`, `IMoneyService`, `ILocalPlayer`) dont
  l'implémentation réelle vit dans la couche qui touche le jeu ; les processeurs de
  messages se testent avec des fakes. Chez nous, la frontière naturelle existe déjà :
  `CoopSession` (orchestration, testable) vs `SteamTransport`/`LocalPlayerLocator`
  (couche jeu/Steam, non testable hors jeu).
- **Tests d'intégration in-game** : mods de self-test et commandes debug (chez nous :
  raccourcis type F9 déjà en place ; ajouter une commande qui fait un « loopback local »
  — héberger et se connecter à soi-même dans le même process — valide tout le pipeline
  message → application d'état sans deuxième compte). Le vrai test à deux comptes Steam
  reste manuel et fait partie de la checklist §5.

### Application à CoopAmbitions

`NetMessage.cs` référence `UnityEngine` (Vector3 dans `PlayerStateData`) — ce n'est
**pas** bloquant : les structs math de `UnityEngine.CoreModule` sont du managed pur,
donc un projet de test .NET classique référençant le package NuGet `UnityEngine.Modules`
(2022.3.x) exécute `Vector3` sans le moteur. Deux niveaux d'ambition :

1. **v0 (zéro friction)** : projet `CoopAmbitions.Tests` (net48 ou net472, MSTest/NUnit)
   qui compile *uniquement* `Scripts/Net/NetMessage.cs` (par `<Compile Include>` pointant
   dans le dossier du mod — pas de duplication) + `UnityEngine.Modules`. Tests :
   round-trip de chaque type de message (pattern Nitrox), garde-fous de taille
   (< 1 Ko pour les messages unreliable, cf. contrainte MTU de la SYNTHESE), tolérance
   aux payloads tronqués/malveillants (un client hostile enverra n'importe quoi).
2. **v1** : extraire la logique pure (protocole, machine à états de session, logique de
   vote de skip, resync d'horloge) dans un sous-dossier `Scripts/Core/` sans référence
   aux DLL BA, couvert par les tests ; la couche `Sync/` et les patches Harmony restent
   testés in-game.

---

## Pipeline recommandé pour CoopAmbitions

### v0 — tout de suite, zéro friction (aucune DLL requise)

1. **Tests du protocole** : créer `Tests/CoopAmbitions.Tests.csproj` (voir §6.1) +
   workflow `ci.yml` (ci-dessous, job `test`) : round-trip de tous les MessageType,
   garde de taille, payloads corrompus. C'est le seul filet possible aujourd'hui — et
   c'est le bon : le protocole est la partie qu'on ne peut pas déboguer facilement in-game.
2. **Format** : `.editorconfig` + `dotnet format --verify-no-changes` dans le même
   workflow.
3. **Veille de MAJ du jeu** : workflow `watch-game.yml` (ci-dessous) — cron quotidien qui
   lit le buildid public de l'app 1331550 via api.steamcmd.net et ouvre une issue quand
   il change. (Note : endpoint vérifié comme pattern en production chez Jotunn ; il est
   inaccessible depuis la sandbox de recherche, à valider au premier run.)
4. **Écrire à Hovgaard Games** pour demander l'autorisation de publier des reference
   assemblies strippées du jeu (ou qu'ils les fournissent avec le SDK). Coût : un
   message ; gain potentiel : toute la colonne « compile » du CI devient publique et
   triviale.
5. **Fichier `COMPATIBILITY.md`** (ou table dans le README) : buildid(s) BA testés ↔
   version du mod, tenu à la main dès maintenant — c'est la donnée que le §4 exploitera.

### v1 — à la première release publique

1. **Refs strippées** : depuis le projet SDK local (DLL importées), passer Refasmer sur
   `Assets/_BaDependencies/GameDlls/` → pousser le résultat dans un **repo privé**
   `coopambitions-game-refs` (versionné par buildid), monté en submodule ou téléchargé
   dans le CI avec un token. Ajouter au `ci.yml` le job `build` : csproj généré façon
   Dudeldups (net472, defines `BA_GAME_DLLS_IMPORTED`…), références = refs strippées +
   `UnityEngine.Modules` → **chaque PR prouve que le mod compile contre l'API du jeu**.
   Passer en NuGet public si/quand Hovgaard donne son accord.
2. **Release tag-driven** (`release.yml` ci-dessous) : tag `vX.Y.Z` → vérification que la
   version du tag == `ModManifest.asset` == constante du mod (pattern SMAPI, en garde-fou
   plutôt qu'en bump auto) → build → zip `CoopAmbitions/` au format `Output/` du SDK
   (DLL + `Locales/` + `Dependencies/` éventuel) → changelog git-cliff → GitHub Release
   (+ pre-release automatique si le tag contient `-beta`).
3. **Workshop** : création de l'item et premières publications **in-game** (Mod Creator).
   Tester une fois `steamcmd +workshop_build_item` sur un item de test ; l'adopter pour
   les mises à jour seulement s'il ne casse pas la fiche. Sinon, assumer l'upload manuel
   — avec le zip de la GitHub Release comme source, c'est 5 minutes.
4. **Runtime** : au chargement, comparer le buildid courant (parsing ACF, comme
   `SteamInstallLocator` du SDK) à la liste testée ; buildid inconnu → bannière claire +
   hébergement désactivé. Handshake réseau : version mod + buildid jeu, refus poli en cas
   de mismatch.
5. **Beta** : pre-releases GitHub `-beta` + canal Discord testeurs (sessions à deux
   planifiées). Pas de second item Workshop tant que la demande n'existe pas.
6. **Template d'annonce « MAJ du jeu »** prêt dans `docs/` (message Workshop/Discord :
   cassé/en cours/ETA), déclenché par l'issue du watcher.

### Exemple de workflow — `.github/workflows/ci.yml`

```yaml
name: CI
on:
  push: { branches: [main] }
  pull_request:

env:
  DOTNET_NOLOGO: true

jobs:
  # ---- v0 : tourne sans aucune DLL du jeu ----
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.x' }
      - name: Format check
        run: dotnet format Tests/CoopAmbitions.Tests.csproj --verify-no-changes
      # Le csproj de test inclut par lien les sources pures du mod
      # (Scripts/Net/NetMessage.cs, futur Scripts/Core/) + PackageReference
      # UnityEngine.Modules 2022.3.* pour Vector3 & co.
      - name: Tests du protocole réseau
        run: dotnet test Tests/CoopAmbitions.Tests.csproj --logger trx
      - uses: actions/upload-artifact@v4
        if: ${{ !cancelled() }}
        with: { name: test-results, path: '**/*.trx' }

  # ---- v1 : compile le mod contre les reference assemblies strippées ----
  build:
    runs-on: ubuntu-latest
    # Le repo privé de refs n'est accessible qu'aux mainteneurs : on saute le job
    # sur les PR de forks au lieu de le faire échouer.
    if: github.event.pull_request.head.repo.fork != true
    steps:
      - uses: actions/checkout@v4
      - uses: actions/checkout@v4
        with:
          repository: <owner>/coopambitions-game-refs   # refs Refasmer, versionnées par buildid
          token: ${{ secrets.GAME_REFS_TOKEN }}         # fine-grained PAT, lecture seule
          path: game-refs
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.x' }
      - name: Compile CoopAmbitions.dll (façon Dudeldups, sans Unity)
        run: |
          # build/gen-csproj.sh reproduit tools/external-build du repo Dudeldups :
          # net472, EnableDefaultCompileItems=false, sources=CoopAmbitions/Scripts/**,
          # Reference=game-refs/*.dll, PackageReference UnityEngine.Modules 2022.3.*,
          # defines BA_GAME_DLLS_IMPORTED;UNITY_2022_3;UNITY_STANDALONE_WIN;NET_4_8
          ./build/gen-csproj.sh game-refs > build/CoopAmbitions.CI.csproj
          dotnet build build/CoopAmbitions.CI.csproj -c Release
      - uses: actions/upload-artifact@v4
        with: { name: CoopAmbitions-dll, path: build/bin/Release/CoopAmbitions.dll }
```

### `.github/workflows/release.yml`

```yaml
name: Release
on:
  push:
    tags: ['v[0-9]+.[0-9]+.[0-9]+*']   # v1.2.3 et v1.2.3-beta.1

permissions:
  contents: write

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }        # git-cliff a besoin de l'historique
      - name: Vérifier la cohérence des versions (pattern SMAPI)
        run: |
          TAG="${GITHUB_REF_NAME#v}"
          MANIFEST=$(sed -n 's/^  Version: //p' CoopAmbitions/ModManifest.asset)
          [ "$TAG" = "$MANIFEST" ] || { echo "Tag v$TAG != ModManifest $MANIFEST"; exit 1; }
      # … job/étapes de build identiques au ci.yml (refs strippées) …
      - name: Paquet au format Output/ du SDK
        run: |
          mkdir -p dist/CoopAmbitions
          cp build/bin/Release/CoopAmbitions.dll dist/CoopAmbitions/
          cp -r CoopAmbitions/Locales dist/CoopAmbitions/
          # Dependencies/ (HarmonyX…) quand on l'embarquera
          cd dist && zip -r "CoopAmbitions-${GITHUB_REF_NAME}.zip" CoopAmbitions
      - name: Changelog
        uses: orhun/git-cliff-action@v4
        id: cliff
        with: { args: --latest --strip header }
      - name: GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          body: ${{ steps.cliff.outputs.content }}
          prerelease: ${{ contains(github.ref_name, '-') }}
          files: dist/CoopAmbitions-*.zip
      # Étape Workshop volontairement absente : upload in-game (Mod Creator),
      # à automatiser via steamcmd workshop_build_item SEULEMENT après le test §3.
```

### `.github/workflows/watch-game.yml`

```yaml
name: Watch Big Ambitions updates
on:
  schedule: [{ cron: '17 6 * * *' }]   # quotidien
  workflow_dispatch:

permissions:
  contents: write
  issues: write

jobs:
  check-buildid:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Comparer le buildid public
        id: check
        run: |
          CURRENT=$(curl -sf "https://api.steamcmd.net/v1/info/1331550" \
            | jq -r '.data."1331550".depots.branches.public.buildid')
          KNOWN=$(cat .game-buildid 2>/dev/null || echo none)
          echo "current=$CURRENT" >> "$GITHUB_OUTPUT"
          echo "changed=$([ "$CURRENT" != "$KNOWN" ] && echo true || echo false)" >> "$GITHUB_OUTPUT"
      - name: Ouvrir une issue + mémoriser
        if: steps.check.outputs.changed == 'true'
        env: { GH_TOKEN: '${{ github.token }}' }
        run: |
          gh issue create \
            --title "MAJ Big Ambitions détectée (buildid ${{ steps.check.outputs.current }})" \
            --body "Nouveau buildid public. À faire : re-importer les DLL dans le SDK, \
          regénérer les refs strippées (Refasmer) dans coopambitions-game-refs, \
          smoke test à 2 comptes, mettre à jour COMPATIBILITY.md, épingler l'annonce si cassé."
          echo "${{ steps.check.outputs.current }}" > .game-buildid
          git config user.name github-actions && git config user.email actions@github.com
          git add .game-buildid && git commit -m "chore: buildid ${{ steps.check.outputs.current }}" && git push
```

### Ce qu'on ne fait délibérément pas

- **Unity headless en CI** : double blocage (licence + DLL), zéro gain tant que le mod
  est code-only — `dotnet build` produit la même DLL (§2).
- **DepotDownloader en CI** : ToS gris, secrets de compte Steam à gérer, pour économiser
  2 minutes de re-strip manuel par MAJ du jeu (§1.d, §4).
- **Publication Workshop automatisée dès le départ** : l'upload in-game est le chemin
  supporté par Hovgaard ; on n'automatise qu'après avoir prouvé l'innocuité de
  `workshop_build_item` sur un item de test (§3).
- **Télémétrie** : hostilité communautaire documentée ; bouton de report de logs à la
  place (§5).
