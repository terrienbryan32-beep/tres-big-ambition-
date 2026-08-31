# Outillage & workflow de développement — comment les moddeurs Unity expérimentés travaillent, et quoi en retenir pour CoopAmbitions

*Recherche du 2026-08-31. Sources : code du SDK officiel (`/home/user/hovgaardgames/bigambitions`),
dépôt réel d'un moddeur Big Ambitions prolifique ([Dudeldups/big-ambitions-mods](https://github.com/Dudeldups/big-ambitions-mods) —
fork du SDK avec ~15 mods et un pipeline de build externe), templates et outils des communautés
Valheim ([JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub)), BepInEx
([BepInEx.Debug](https://github.com/BepInEx/BepInEx.Debug), [BepInEx.Templates](https://github.com/BepInEx/BepInEx.Templates)),
Lethal Company (GameLibs), [Krafs.Publicizer](https://github.com/krafs/Publicizer),
[UnityExplorer](https://github.com/sinai-dev/UnityExplorer), docs dnSpy/dnSpyEx et BepInEx.*

---

## TL;DR — les 7 décisions

| # | Décision | Pourquoi |
|---|---|---|
| 1 | **Boucle chaude = build externe `dotnet build` → copie dans `ModsLocal`**, sans passer par Unity | C'est ce que fait Dudeldups (le moddeur BA le plus actif) ; cycle de quelques secondes au lieu de ~1 min de focus Unity + AssemblyBuilder |
| 2 | **Tester en priorité le reload à chaud in-game** (désactiver/réactiver le mod dans le menu Mods) — c'est l'expérience n°1 à mener | `OnLoadAsync`/`OnUnloadAsync` existent pour ça ; si le jeu relit la DLL du disque, on économise un restart de jeu par itération |
| 3 | **dnSpy + mono.dll débogable** pour les breakpoints (un fork existe pour la version *exacte* 2022.3.62f2) ; **UnityExplorer Standalone Mono** embarqué dans un mod compagnon dev pour l'inspection live | Ce sont les deux outils standard de tous les moddeurs Unity Mono |
| 4 | **Jamais de DLL du jeu dans le repo** ; accès aux membres privés par **couche Interop réflexion (AccessTools/Traverse de HarmonyX)** d'abord, publicizer seulement en secours dans la voie de build externe | Le repo Dudeldups gitignore les DLL ; ses mods utilisent la réflexion ; le build final Workshop doit rester compilable par le Mod Builder (DLL non publicisées) |
| 5 | **Notre repo reste la source de vérité, lié par junction NTFS dans `Assets/Mods/` du clone SDK** ; les `.meta` du dossier du mod sont commités | Pattern éprouvé (Unity suit les junctions) ; garde notre historique git propre et le SDK jetable/re-clonable |
| 6 | **Config à trois étages** : `ModOptions` (UI native, joueur) · JSON dans `persistentDataPath` (dev/avancé, hot-reload) · `Config/` du mod (données livrées, pas des préférences) | Transposition directe de BepInEx `ConfigEntry`/MelonPreferences aux moyens du SDK BA ; le dossier du mod est écrasé à chaque install |
| 7 | **Un `LoopbackTransport` pour développer la synchro en solo** | Le vrai goulot du dev multijoueur, c'est « il faut 2 comptes Steam et 2 machines » ; 90 % de la logique de réplication se teste sans Steam |

---

## 1. La boucle d'itération rapide

### 1.1 Notre boucle « officielle » et ce qu'elle coûte

Le chemin prévu par le SDK :

1. Éditer le code dans Rider/VS (le projet Unity génère les `.csproj`, IntelliSense complet sur les DLL du jeu).
2. Refocus Unity → recompilation des asmdef (domain reload : 10–60 s sur ce projet HDRP).
3. `Big Ambitions → Mod Builder → Build & Install` : recompilation **séparée** en mode Player via
   `AssemblyBuilder` (un seul build à la fois, file d'attente), validation (13 règles), AssetBundles
   éventuels, copie vers `Output/<ModId>/` puis **écrasement** de
   `%LocalAppData%..\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\<ModId>\`
   (`ModInstaller.Install` supprime et recopie le dossier ; chemin overridable via l'EditorPrefs `BAModBuilder.ModsLocalPath`).
4. Lancer le jeu, menu Mods, activer le mod, charger une partie, aller tester.

Coût réel d'un aller-retour : **2 à 5 minutes**, dominé par le focus Unity, le lancement du jeu et le
chargement de save. Tout l'outillage ci-dessous vise à raboter chaque segment.

### 1.2 Ce que font les autres communautés : auto-déploiement MSBuild

Pattern universel chez BepInEx/Valheim/Lethal Company — le build **déploie tout seul** :

- **JotunnModStub** (template officiel Valheim) : le `.csproj` a un target
  `AfterTargets="Build"` qui exécute `scripts/publish.ps1`. En **Debug** : copie
  `<mod>.dll` + `.pdb` + `.dll.mdb` (généré par `pdb2mdb.exe` embarqué dans le repo) vers
  `$(VALHEIM_INSTALL)\BepInEx\plugins\<mod>\` (ou `MOD_DEPLOYPATH`). En **Release** : construit un
  zip Thunderstore (`plugins/` + `manifest.json` + `README.md`) dans `Packages/`.
  Les chemins viennent de **variables d'environnement** (`VALHEIM_INSTALL`, `MOD_DEPLOYPATH`) —
  rien de machine-spécifique n'est commité.
- **BepInEx.PluginTemplate / docs BepInEx** : même idée avec un fichier
  `solution_private.targets` (gitignoré) définissant `GameDir`, et un post-build
  `Copy` vers `$(GameDir)\BepInEx\plugins`.

Leçon : *le build qui n'installe pas est un build à moitié fini*. Notre équivalent : un script qui
enchaîne compilation → copie dans `ModsLocal` (voir §8).

### 1.3 Le pipeline de build externe de Dudeldups — LE modèle à copier

Le repo `Dudeldups/big-ambitions-mods` (fork du SDK avec 15 mods : BigHax, CameraTools,
StreetQuestRPG, Taxi!, …) contient `tools/external-build/BuildBigAmbitionsMods.ps1` +
`mods.externalbuild.json`. C'est la réponse d'un moddeur BA expérimenté au coût du Mod Builder :
**compiler le C# du mod SANS ouvrir Unity**.

Ce que fait le script (à répliquer quasi tel quel) :

- Découvre les mods sous `Assets/Mods/<X>/Scripts/**/*.cs` (exclut `Editor/`, `bin/`, `obj/`,
  et `UnitySourceGeneratedAssemblyMonoScriptTypes_v1.cs`), overrides par mod dans un JSON
  (`modName`, `assemblyName`, `modsLocalFolder`, `enabled`).
- **Génère un csproj SDK-style jetable** (`obj/ExternalModBuild/<mod>/`) :
  - `TargetFramework=net472` (commentaire du script : *« Big Ambitions 1.0 runs on Unity's classic
    .NET Framework profile ; netstandard2.1 assemblies cannot be discovered because the player does
    not ship netstandard.dll »* — ne jamais cibler netstandard) ;
  - `DefineConstants = BA_GAME_DLLS_IMPORTED;UNITY_2022_3;UNITY_2022_3_OR_NEWER;UNITY_2022;UNITY_STANDALONE;UNITY_STANDALONE_WIN;UNITY_64;NET_4_6;NET_4_8;NET_FRAMEWORK` (imiter les defines Unity pour que le même source compile dans les deux pipelines) ;
  - références (`Private=false` partout) : tous les DLL de `Assets/_BaDependencies/GameDlls/`,
    les `UnityEngine*.dll` de `<UnityEditor>\Editor\Data\Managed\UnityEngine`, et les assemblies de
    packages déjà compilées par Unity dans `Library/ScriptAssemblies` (filtrées `Unity.*`, hors `.Editor`) ;
  - `EnableDefaultCompileItems=false` + liste explicite des sources.
- `dotnet build` (Release), vérifie le nom d'assembly produit, puis **`-Install`** : copie la DLL +
  `thumbnail.png` + `Locales/` + `Config/` + `Layouts/` + `AssetBundles/` dans
  `ModsLocal\<mod>\` (avec retry sur fichiers verrouillés — le jeu peut tourner pendant la copie).

Usage : `.\BuildBigAmbitionsMods.ps1 -ModName CoopAmbitions -Install`. Cycle : **~5 s**.

Points d'attention qu'il révèle :
- Le build externe ne fait **ni validation ni AssetBundles ni manifest** — il resynchronise juste la
  DLL et les fichiers plats d'un mod **déjà installé une fois** par le Mod Builder. Le Mod Builder
  reste le build « canonique » (avant un test complet, avant publication).
- Garder les deux pipelines d'accord : mêmes sources, mêmes références (la liste
  `CanonicalGameDlls.All` du SDK = la liste `precompiledReferences` de l'asmdef), mêmes defines.

### 1.4 Alternative : Unity en batch mode

Pour un build « canonique » scriptable sans ouvrir l'éditeur :
`Unity.exe -batchmode -quit -projectPath <SDK> -executeMethod <classe statique>` — il faut écrire
une petite méthode éditeur statique qui appelle `ModPackager.Enqueue(mod, install:true)` et attend la
fin du job (l'API s'y prête : événement `ModPackager.JobChanged`, état `IsTerminal`). Coût : le
démarrage d'Unity (~1-2 min sur ce projet) — utile pour la CI, pas pour la boucle chaude.

### 1.5 Rechargement à chaud : exploiter `OnLoadAsync`/`OnUnloadAsync`

Faits établis :

- L'interface impose la symétrie : `OnLoadAsync(ModContext)` / `OnUnloadAsync()`, et les exemples du
  SDK défont *réellement* tout au unload (dé-enregistrement d'items, `OptionsService.RemoveModOptions`,
  restauration des tableaux patchés). Le menu **Mods du jeu sait activer/désactiver un mod sans
  redémarrer** — c'est le mécanisme sur lequel s'appuyer.
- Limite CLR : sous Mono, **une assembly chargée ne se décharge jamais** de l'AppDomain. Deux
  scénarios possibles côté jeu : (a) au ré-enable, le jeu relit la DLL depuis `ModsLocal` et la
  charge via `Assembly.Load(byte[])` → chaque version cohabite, la nouvelle prend la main : **le
  hot reload marche** ; (b) le jeu réutilise l'assembly déjà chargée → seule l'activation/désactivation
  est à chaud, pas la mise à jour du code.

**Expérience n°1 à mener** (10 min, détermine tout le workflow) : builder le mod avec un
`Logger.Info("v1")`, lancer le jeu, activer ; rebuilder avec `"v2"` + `-Install` **jeu ouvert** ;
désactiver puis réactiver le mod dans le menu ; regarder le log. Si « v2 » apparaît → notre boucle
devient *modifier → build externe 5 s → toggle in-game 10 s*, sans jamais relancer le jeu.
(À re-tester aussi : toggle pendant qu'une partie est chargée vs depuis le menu principal.)

Si le jeu ne relit pas la DLL, le plan B éprouvé est le pattern **ScriptEngine** (BepInEx.Debug) :
un plugin stable qui charge les plugins « de travail » depuis un dossier `scripts/`, et sur F6 (ou
FileSystemWatcher + délai) recharge tout : lecture de la DLL **en bytes**, **renommage de
l'assembly à chaque chargement** (sinon Mono ressert l'ancienne version de même nom),
`Destroy(oldGameObject)` + `Harmony.UnpatchSelf()` dans `OnDestroy`. Transposé à BA : un mod
`CoopAmbitions.DevHost` (celui que référence le manifest) qui, en mode dev, charge
`CoopAmbitions.Core.dll` depuis un sous-dossier non standard du mod (ex. `DevPayload/`, que le jeu
n'auto-charge pas) via `Assembly.Load(File.ReadAllBytes(...))` et le recharge sur hotkey en rejouant
notre propre cycle unload/load. À ne construire **que** si l'expérience n°1 échoue — et exiger la
même discipline de teardown que `OnUnloadAsync` (elle nous sert de toute façon).

Règle d'architecture qui rend tout reload possible : **tout ce que le mod crée doit être
enregistré dans un registre central** (GameObjects, patches Harmony, sockets, callbacks
`GlobalEvents`) et `OnUnloadAsync` le vide intégralement. C'est déjà notre exigence coop
(déconnexion propre) — le hot reload l'obtient gratuitement.

### 1.6 Lancer le jeu vite, arriver vite en situation de test

- **Lancement** : `steam://rungameid/1331550` (ou l'exe direct — mais Facepunch.Steamworks exige
  Steam démarré, et un exe Steam lancé hors Steam se relance souvent via Steam : garder l'URI).
  Arguments Unity standard utiles (documentés Unity 2022.3, confirmés utilisés par la communauté BA
  pour le troubleshooting) : `-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -windowed`
  (fenêtré = alt-tab rapide vers l'IDE ; deux instances côte à côte le jour où on teste à deux
  machines), `-logfile <chemin>` pour rediriger le log. Le splash Unity n'est pas skippable côté
  joueur, mais BA a une option **« skip intro » dans ses réglages** — l'activer sur le profil de dev.
- **Arriver en jeu** : pas d'argument « charge cette save » côté Unity. Deux leviers :
  1. Explorer `BigAmbitions.DebugMode.dll` dans dnSpy (déjà dans notre liste dnSpy) : le jeu
     embarque un mode debug (le menu pause a des options de déblocage) — chercher un flag/une clé
     PlayerPrefs qui l'active, et des commandes toutes faites (set money/time/teleport).
  2. **Le faire nous-mêmes dans le mod, en mode dev** : un `[ModEntryOnMainMenuLoad]` qui, si
     `dev.json` contient `"autoLoadSave": "CoopTest"`, appelle le chargement de save du jeu
     (chercher l'API `SaveGameManager`/UI de load dans dnSpy), puis si `"autoHost": true` héberge
     automatiquement (notre F9), ou `"autoJoin": "<steamid>"` tente de rejoindre. C'est le levier le
     plus rentable : le jeu démarre et se met tout seul dans la situation à tester.
- **Save de test dédiée** : une save « CoopTest » minimale (perso créé, tutoriel passé, un peu
  d'argent) copiée dans le dossier de saves et versionnée dans `tools/fixtures/` — recharger un état
  connu plutôt que rejouer l'onboarding.

### 1.7 Tester du multijoueur sans deuxième joueur

Le vrai coût de la boucle coop n'est pas la compilation, c'est le **deuxième client**. Deux
mitigations à outiller :

1. **`LoopbackTransport`** : derrière la même interface que `SteamTransport`, un transport en
   mémoire qui fait tourner « hôte » et « client fantôme » dans le même process (ou rejoue un
   enregistrement de paquets). Toute la logique de réplication (sérialisation, snapshots,
   suppression d'écho, horloge) se développe ainsi en solo, sans Steam. Le transport Steam ne se
   re-teste que lors des sessions à deux.
2. **Deux machines, deux comptes** pour les vrais tests (Steam interdit deux sessions du même
   compte en jeu simultanées). Un deuxième PC/portable avec le build installé + le script de
   déploiement pointé dessus (partage réseau du dossier `ModsLocal`) rend la session à deux
   raisonnablement fluide. Prévoir un créneau « test à deux » par jalon plutôt que par itération.

---

## 2. Debugging

### 2.1 Les logs (le quotidien)

- **Player.log** : `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\Player.log`
  (+ `Player-prev.log` du run précédent) — chemin Unity standard `LocalLow\<Company>\<Product>`,
  cohérent avec l'emplacement de `ModsLocal`. Tout `Debug.Log`, les exceptions et nos
  `context.Logger.*` y finissent. Réflexe à outiller : une console qui tail en continu
  (`Get-Content -Wait -Tail 50 $log` / `tail -f`), avec un filtre sur notre préfixe.
- **`context.Logger`** (`Info`/`Warn` attestés) : préfixer chaque message d'un tag de domaine
  (`[Coop.Net]`, `[Coop.Sync]`) pour le filtrage. Pattern Dudeldups à copier : wrapper statique avec
  **flag debug** (silence par défaut, verbeux si l'option dev est cochée) et **`WarnOnce(key, msg)`**
  (un `HashSet<string>` de clés déjà émises) — indispensable pour du code appelé chaque frame.
- **Logger fichier maison** : CameraTools écrit via `File.AppendAllText` sous
  `Application.persistentDataPath/CameraTools/`. Contre-exemple : BigHax a **neutralisé** son file
  logger avec le commentaire *« Big Ambitions 1.0's player profile strips the System.IO APIs that
  the legacy development logger used »*. À vérifier sur la 1.0 courante : si `File.*` casse dans le
  player, tout loguer via `Debug.Log`/`context.Logger` (donc Player.log) et n'écrire des fichiers
  que défensivement (try/catch, jamais sur le chemin du gameplay).
- **Overlay in-game** : StreetQuestRPG embarque un `DebugOverlay` IMGUI (`OnGUI`/`GUILayout`)
  togglable par hotkey ; Pink a un « live debug » sur F4. Pour le coop, un overlay
  réseau (état connexion, ping, paquets/s par type, dernière erreur) togglable **F8** vaut mille
  logs — à construire dès la phase MVP.

### 2.2 Breakpoints : dnSpy sur le jeu (route canonique Unity Mono)

Procédure standard de la communauté (docs BepInEx + wiki dnSpy/dnSpyEx), applicable telle quelle à BA :

1. Prendre **dnSpyEx** (fork maintenu de dnSpy).
2. Remplacer le runtime Mono du jeu par une version compilée avec le débogueur :
   `Big Ambitions\MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll` ← build debug correspondant à
   la version Unity. Bonne nouvelle : il existe un fork dédié à **notre version exacte**
   ([dnSpy-Unity-mono-unity2022.3.62f2](https://github.com/VitaminStack/dnSpy-Unity-mono-unity2022.3.62f2),
   base [dnSpyEx/dnSpy-Unity-mono](https://github.com/dnSpyEx/dnSpy-Unity-mono)). Garder l'original
   sous `mono-2.0-bdwgc.dll.bak`.
3. Ce mono lit la variable d'env `DNSPY_UNITY_DBG2`
   (`--debugger-agent=transport=dt_socket,server=y,address=127.0.0.1:55555,defer=y,no-hide-debugger`) ;
   sans elle il écoute sur 55555 par défaut.
4. Dans dnSpy : ouvrir notre DLL depuis `ModsLocal\CoopAmbitions\` **et** les DLL du jeu depuis
   `Big Ambitions_Data\Managed\` → `Debug > Start Debugging`, moteur **Unity**, exe du jeu (ou
   moteur **Unity (Connect)** sur 127.0.0.1:55555 pour un jeu déjà lancé via Steam). Breakpoints
   directement dans le code décompilé du jeu comme dans notre mod.
5. Caveats connus : Steam peut re-télécharger le mono.dll original à la « vérification des
   fichiers » (re-copier après chaque update) ; le Simple Mono Profiler est incompatible avec ce
   mono custom ; une DLL chargée à chaud par bytes reste débogable (dnSpy montre les modules en mémoire).

Symboles : compiler la config Debug avec `DebugType=full` et copier le `.pdb` (et un `.mdb` généré
par `pdb2mdb`, comme le fait JotunnModStub) à côté de la DLL dans `ModsLocal` — dnSpy s'en sert pour
les noms de variables ; sans eux le décompilé reste utilisable.

### 2.3 Variante confort : Rider / Visual Studio Tools for Unity

Pour déboguer notre code de mod dans l'IDE (breakpoints dans nos vrais sources) plutôt que dans le
décompilé :
- L'attache VSTU/Rider ne voit que les players en **Development Build** — un jeu Steam release n'y
  apparaît pas tel quel.
- Deux contournements documentés par les communautés :
  1. **Unity Doorstop 4** (le `winhttp.dll` de BepInEx, utilisable seul) : `doorstop_config.ini`
     avec `debug_enabled = true`, `debug_address = 127.0.0.1:10000`, `debug_suspend = false` —
     active l'agent de débogage du mono **du jeu**, sans swap de DLL ; ensuite « Attach Unity
     Debugger → Input IP » 127.0.0.1:10000 dans VS/Rider (route décrite par
     [UnityDebugModeInstall](https://github.com/NBKRedSpy/UnityDebugModeInstall) pour
     BepInEx/Doorstop 4+).
  2. Transformer l'install en dev build (copie des players de dev depuis l'éditeur +
     `boot.config` avec `player-connection-debug=1`) — plus invasif, à refaire à chaque update.
- Recommandation : commencer par dnSpy (zéro ambiguïté, tout le monde documente ce chemin), et
  monter Doorstop+Rider seulement si on passe beaucoup de temps en pas-à-pas dans notre propre code.

### 2.4 Inspection live : UnityExplorer (l'outil n°1 des moddeurs Unity)

[UnityExplorer](https://github.com/sinai-dev/UnityExplorer) fournit in-game : explorateur de scène,
inspecteur d'objets/components avec édition à chaud, recherche d'objets/types, **console C# REPL**
(`Mono.CSharp.Evaluator` — tester une ligne d'API du jeu sans recompiler !), hook de méthodes,
inspecteur de la hiérarchie UI, log viewer. Existe en release **Standalone Mono** faite pour « any
injector or loader of your choice » :

1. Déposer `UnityExplorer.Standalone.Mono.dll` + ses dépendances (UniverseLib, HarmonyX/MonoMod —
   fournies dans la release `UnityExplorer.Editor`) dans `Dependencies/` d'un **mod compagnon
   `CoopDevTools`** (le SDK charge les DLL de `Dependencies/`, mécanisme déjà prévu pour HarmonyX).
2. Dans son `OnLoadAsync` : `UnityExplorer.ExplorerStandalone.CreateInstance();` (+ abonnement
   `OnLog`). Toggle UI : F7 par défaut.
3. Ce mod compagnon reste **local, jamais publié** (c'est aussi le bac à sable pour hotkeys de
   triche dev : argent, téléport, heure).

Usage type pour nous : vérifier à la main `GameManager.Instance.playerController`, fouiller
`SaveGameManager.Current`, trouver le bon transform d'avatar, tester
`VehicleHelper.CreateAndSpawnVehicle` en REPL avant d'écrire le code de réplication.

Attention : le projet sinai-dev est archivé ; si la release ne marche pas sur 2022.3/HDRP, prendre
un fork maintenu (yukieiji/UnityExplorer, CinematicUnityExplorer) — même API.

### 2.5 Boîte à outils complémentaire

- **`BigAmbitions.DebugMode.dll`** (livrée avec le jeu, référencée par les asmdef du SDK !) : à
  explorer dans dnSpy en priorité — c'est l'outillage de debug du studio, potentiellement activable.
- **Simple Mono Profiler** (BepInEx.Debug) : profileur CSV pour player Mono — si un jour la
  réplication fait ramer le jeu ; incompatible avec le mono dnSpy, l'un ou l'autre.
- **DemystifyExceptions** (idem) : stacktraces lisibles (résout lambdas/énumérateurs) — nécessite
  un chargement précoce type doorstop, à considérer seulement si on adopte Doorstop.
- **dnSpy comme éditeur d'IL** : pour tester un patch « et si cette méthode retournait toujours
  true ? » sans écrire le patch Harmony, éditer la méthode dans dnSpy et sauver l'assembly modifiée
  (sur une copie du jeu !). Réservé à l'exploration.

---

## 3. Compiler contre le jeu : références, publicizers, légal

### 3.1 Ce que fait notre SDK (et pourquoi c'est déjà le bon pattern)

Le SDK résout « comment référencer le jeu sans distribuer le jeu » proprement :

- La Welcome window **importe les 32 DLL canoniques** (`CanonicalGameDlls.All`) depuis
  `<Steam>\Big Ambitions\Big Ambitions_Data\Managed\` vers `Assets/_BaDependencies/GameDlls/`,
  trace le **buildid Steam** (état `UpdateAvailable` quand le jeu a été mis à jour → réimporter),
  fixe les GUID des `.meta` et pose le define global `BA_GAME_DLLS_IMPORTED` (le define est
  volontairement machine-local : un `defineConstraints` sur les asmdef des mods fait qu'un clone
  frais **sans DLL** ne casse pas la compilation, il ne compile juste pas les mods).
- Chaque DLL importée est `isExplicitlyReferenced: 1` : l'asmdef du mod doit poser
  `overrideReferences: true` + la liste **complète** des 32 `precompiledReferences` (le validator a
  une règle de dérive avec quick-fix).
- Le `.gitignore` (identique chez Dudeldups) exclut `Assets/_BaDependencies/GameDlls/**/*.dll` —
  **les DLL du jeu ne sont jamais commitées**, chacun les régénère depuis SA copie du jeu.

C'est exactement l'équivalent du pattern `GamePath`/props utilisateur des communautés csproj — mais
géré par un outil éditeur. Rien à changer, tout à respecter : notre repo ne doit contenir **aucune**
DLL du jeu, et notre doc d'onboarding doit dire « importe via la Welcome window ».

### 3.2 Comment les communautés csproj gèrent les chemins (pour notre build externe)

- **Variables d'env** (`VALHEIM_INSTALL`, `MOD_DEPLOYPATH` chez Jotunn) ou **fichier de props
  utilisateur gitignoré** (`solution_private.targets`, `*.csproj.user`, `GamePath.props`) : le
  csproj commité ne contient jamais de chemin absolu.
- **Packages NuGet de références** : la voie industrialisée de Lethal Company —
  [`LethalCompany.GameLibs.Steam`](https://www.nuget.org/packages/LethalCompany.GameLibs.Steam)
  publie des assemblies **strippées** (corps de méthodes retirés par NStrip : plus le code du jeu,
  seulement les signatures) **et publicisées**, régénérées automatiquement à chaque update du jeu
  (« GameLib Dehumidifier » ; template générique :
  [Raicuparta/unity-libs-nuget](https://github.com/Raicuparta/unity-libs-nuget)). Avantage : CI et
  nouveaux contributeurs compilent **sans posséder le jeu**. Statut légal : zone grise tolérée
  (le stripping retire l'implémentation — il ne reste que l'API) ; l'équivalent BA n'existe pas et
  ce n'est pas à nous de le publier tant qu'on n'a pas l'aval de Hovgaard. En privé, rien n'empêche
  un feed NuGet/dossier partagé **d'équipe** avec des refs strippées générées par
  `BepInEx.AssemblyPublicizer.Cli --strip-only` si on veut une CI compilante sans le jeu.

### 3.3 Accès aux membres privés : les trois écoles

| Approche | Outil | Comment ça marche | Où ça s'applique chez nous |
|---|---|---|---|
| **Réflexion optimisée** | HarmonyX `AccessTools`/`Traverse`, delegates cachés (`AccessTools.FieldRefAccess`, `MethodDelegate`) | Résolution runtime, coût amorti par cache | ✅ Partout : marche dans le pipeline Unity SDK **et** dans le build externe ; c'est ce que font les mods Dudeldups (helpers `GetMemberValue`/`SetMemberValue`) |
| **Publicizer au build** | [Krafs.Publicizer](https://github.com/krafs/Publicizer) (MSBuild : items `<Publicize Include="BigAmbitions:GameManager.playerController"/>`, ou `<PublicizeAll>`) ; [BepInEx.AssemblyPublicizer](https://github.com/BepInEx/AssemblyPublicizer) (lib + CLI) ; [NStrip](https://github.com/bbepis/NStrip) (CLI, aussi strip) | Copie des DLL réécrites `public` fournies **au compilateur seulement** ; au runtime, l'IL compilée accède aux vrais membres privés — accepté par le Mono d'Unity (les checks d'accès n'y sont pas appliqués pour l'IL déjà compilée ; Krafs fournit en plus `IgnoresAccessChecksTo` pour les runtimes stricts) | ⚠️ Seulement dans la **voie build externe** (csproj généré → y injecter Krafs.Publicizer est trivial). Incompatible avec le pipeline Unity/Mod Builder : Unity compile contre les DLL de `GameDlls/`, et les remplacer par des publicisées se fait écraser au prochain réimport + rend le projet non-standard |
| **Patch Harmony** | HarmonyX (embarqué via `Dependencies/`) | Prefix/postfix accèdent aux instances via `__instance`, aux champs privés via `___fieldName` | ✅ Déjà notre plan pour les hooks (ChangeMoneySafe etc.) — les paramètres injectés évitent la réflexion manuelle dans les patches |

**Décision recommandée** : couche `Interop/` unique qui centralise TOUS les accès non publics au jeu
(un fichier par domaine : `Interop.Player`, `Interop.Time`, `Interop.Save`…), implémentée en
`AccessTools`+delegates cachés. Bénéfices : compile dans les deux pipelines sans trucage, un seul
endroit à corriger à chaque update du jeu, et testable (le reste du code ne voit que notre façade).
Le publicizer reste une carte en réserve si un accès devient trop pénible — auquel cas il vit
uniquement dans le csproj du build externe, et le code concerné doit rester compilable côté Unity
(`#if EXTERNAL_BUILD` en dernier recours — à éviter).

### 3.4 Résumé légal

- ❌ Jamais dans le repo : DLL du jeu, DLL publicisées du jeu, AssetBundles extraits du jeu.
- ⚠️ Zone grise tolérée par l'écosystème : refs strippées (API sans code) — en privé/équipe OK,
  publication publique seulement avec l'accord de Hovgaard (ils sont modding-friendly : demander !).
- ✅ Toujours OK : notre code (open source prévu), le SDK (MIT), les scripts qui *pointent* vers
  l'install Steam locale de chacun.

---

## 4. Structure de projet

### 4.1 Le dilemme quand le loader impose un format

Le SDK impose : le mod est un dossier sous `Assets/Mods/` d'un projet Unity précis, avec
`ModManifest.asset` (ScriptableObject à GUID), asmdef au format exact, `.meta` partout. Trois
organisations observées :

1. **Fork du SDK, mods dedans** (choix de Dudeldups) : simple, tout est au même endroit, mais
   l'historique git mélange SDK et mods, et un update du SDK = merge upstream. Son `.gitignore`
   montre le prix : exclusions de `Library/`, `*.csproj`, `Output/`, des DLL importées, etc.
2. **Repo de mod séparé + lien dans `Assets/Mods/`** (pattern classique des studios Unity pour
   partager du code entre projets) : le SDK reste un clone jetable non versionné par nous ; notre
   repo ne contient que notre matière.
3. **Sous-module/UPM package** : surdimensionné ici (le Mod Builder découvre les mods par dossier,
   pas par package).

**Recommandé : option 2.** Notre repo est déjà structuré pour (le dossier `CoopAmbitions/` à la
racine). Le lien :

- Windows : `mklink /J <SDK>\Assets\Mods\CoopAmbitions <repo>\CoopAmbitions` — une **junction**
  (pas de droits admin, contrairement à `mklink /D` ; Unity suit les junctions sans configuration —
  usage répandu, cf. [unity-symlink-utility](https://github.com/karl-/unity-symlink-utility)).
- macOS/Linux : `ln -s`.
- Caveats connus : supprimer le dossier **depuis Unity** supprime les vrais fichiers à travers le
  lien (ne jamais « Delete » le dossier du mod dans le Project browser) ; certains watchers voient
  les changements avec un léger délai ; `Library/` du SDK reste hors VCS comme toujours.

### 4.2 Les `.meta` : à versionner, côté mod

Le manifest (`ModManifest.asset`) référence l'asmdef et le dossier `Locales` **par GUID Unity** ;
l'asmdef des exemples référence `BAModAPI` par GUID (`GUID:776d03a35f1b52c4a9aed9f56d7b4229`,
stable car le SDK versionne ses propres `.meta`). Donc : **tous les `.meta` sous `CoopAmbitions/`
sont commités** (sinon chaque clone regénère des GUID et casse le manifest). Le `.gitattributes`
peut marquer `*.asset`/`*.meta` en `text eol=lf` (sérialisation YAML Unity).

### 4.3 Épingler les versions

Un fichier `docs/VERSIONS.md` (ou en tête du README) tenu à jour :
commit du SDK testé, version Unity (2022.3.62f2), **buildid Steam du jeu** contre lequel les DLL
ont été importées (le SDK l'affiche), version HarmonyX embarquée. Chaque update du jeu invalide
potentiellement l'API → ce fichier est le point de départ du diagnostic « ça compilait hier ».

---

## 5. Templates & générateurs : ce qu'ils automatisent (et qu'on doit imiter)

Ce que fournissent `dotnet new bepinex5plugin` ([BepInEx.Templates](https://github.com/BepInEx/BepInEx.Templates)),
JotunnModStub, les templates Lethal Company et le scaffolding tModLoader — traduit en équivalents CoopAmbitions :

| Automatisation chez eux | Équivalent à mettre en place chez nous |
|---|---|
| Projet neuf en 1 commande, TFM/LangVersion/refs corrects d'avance | Notre squelette existe déjà ; ajouter `tools/new-module.ps1` si on multiplie les asmdef, sinon rien |
| **Version single-source** : `<Version>` MSBuild → codegen `MyPluginInfo.PLUGIN_VERSION` (BepInEx.PluginInfoProps) → l'attribut plugin ne peut pas dériver | Une constante `CoopVersion.cs` générée/vérifiée par script, utilisée par le handshake réseau ET affichée dans les logs ET recopiée dans le manifest — le handshake versionné (déjà codé) doit lire cette source unique |
| Chemins machine via env vars / `*.user` gitignoré | `tools/localconfig.sample.json` → copié en `localconfig.json` (gitignoré) : chemin SDK, chemin Unity, `ModsLocal` override, chemin du 2e PC de test |
| Post-build deploy (publish.ps1) | `tools/build.ps1 -Install` (§6 et §8) |
| Packaging release (zip Thunderstore + manifest + README + icône) | `tools/package.ps1` : lance le Mod Builder canonique (ou vérifie `Output/`), zippe `Output/CoopAmbitions` + README + thumbnail pour GitHub Releases (le Workshop, lui, s'upload depuis le jeu) |
| Debug F5 : profils `launchSettings.json` qui lancent le jeu (tModLoader : « Start Client/Server ») | `tools/run.ps1` : build externe → install → `start steam://rungameid/1331550` → tail du Player.log dans la console |
| CI GitHub Actions : le template compile sur push (LC via GameLibs NuGet) | Optionnel : CI de compile avec refs strippées privées (cf. §3.2) ; sinon CI limitée au lint/format + tests du protocole (NetMessage se teste sans le jeu !) |
| `.gitignore`/`.gitattributes` corrects d'emblée | Copier les exclusions du fork Dudeldups (Unity + Output/ + GameDlls) pour le jour où on versionne un projet SDK |
| Analyzers (BepInEx.Analyzers signale les pièges d'API) | Rien d'équivalent BA ; un `.editorconfig` + activation des analyzers Unity de Rider suffit |

---

## 6. Config & options de mod

### 6.1 Les modèles de référence

- **BepInEx `ConfigEntry`** : `Config.Bind("Section", "Key", défaut, new ConfigDescription("…", AcceptableValueRange))` →
  fichier texte `BepInEx/config/<guid>.cfg` généré avec les descriptions en commentaires,
  relu/rechargeable, événement `SettingChanged`, et le plugin **ConfigurationManager** génère une UI
  in-game automatique depuis les métadonnées (F1). Leçons : *déclaratif, défauts + bornes + docs au
  même endroit, UI dérivée gratuitement, notification de changement*.
- **MelonPreferences** : `CreateCategory`/`CreateEntry`, TOML `UserData/MelonPreferences.cfg`,
  même philosophie.

### 6.2 Ce que le SDK BA offre, et comment les vrais mods s'en servent

- **`ModOptions` + `OptionsService.Register(modId, options)`** : UI **native** dans les réglages du
  jeu — `AddHeader/AddToggle/AddSlider(min,max,suffixLocKey)/AddDropdown/AddSplitter`, chaque
  contrôle a un `saveKey` + callback typé. Les labels sont des **clés de locale** (nos `Locales/en.json`, `fr.json`).
  `RemoveModOptions` au unload (obligatoire pour le hot reload).
- **Persistance** : BigHax (Dudeldups) lit/écrit ses réglages via `UnityEngine.PlayerPrefs`
  (clés préfixées par modId, avec migration de clés legacy) — preuve que la persistance des options
  côté jeu est PlayerPrefs-compatible et qu'un mod peut lire ses propres valeurs au chargement pour
  reconstruire l'UI. Pattern complet observé : `XxxSettings` (POCO de l'état) +
  `XxxOptionIds` (constantes de saveKeys) + `XxxOptionPersistence` (Load*/Save* PlayerPrefs) +
  `XxxNativeOptionsUi` (déclaration ModOptions).
- **`context.ModRootPath`** (attesté chez StreetQuestRPG) : chemin du dossier installé du mod →
  StreetQuestRPG y lit `Config/quests.json`, `Config/Characters/*.json` comme **données livrées**
  (contenu moddable par l'utilisateur final). ⚠️ Ce dossier est **écrasé à chaque Build & Install
  et à chaque update Workshop** : n'y stocker aucune préférence utilisateur.

### 6.3 Design retenu pour CoopAmbitions — trois étages

1. **Options joueur → `ModOptions`** (UI native, persistance saveKey/PlayerPrefs) :
   toggle « auto-héberger au chargement de la partie », dropdown hotkey d'hébergement,
   slider tick-rate avatars (10–20 Hz), toggle « logs verbeux ».
   Implémenter le quadriptyque Settings/OptionIds/Persistence/OptionsUi à la BigHax.
2. **Config dev → JSON dans `Application.persistentDataPath/CoopAmbitions/dev.json`**
   (survit aux réinstalls du mod, hors Workshop) : `autoLoadSave`, `autoHost`, `autoJoin`,
   `useLoopbackTransport`, `logPackets`, niveaux de logs par domaine. Chargée au `OnLoadAsync`,
   **rechargée sur hotkey** (F10) — l'équivalent du reload de `.cfg` BepInEx. Absente = tous les
   flags off = comportement release : le fichier n'existe que sur les machines de dev.
3. **Données livrées → `CoopAmbitions/Config/` dans le mod** (lu via `context.ModRootPath`) :
   uniquement du contenu qu'on veut que les joueurs puissent inspecter/modifier (ex. table des
   messages réseau documentée, presets). Rien d'obligatoire au début.

Toute lecture de fichier sous try/catch avec repli sur les défauts (cf. l'alerte System.IO §2.1).

---

## 7. Récap des mécanismes SDK utiles au workflow (référence rapide)

- Menu `Big Ambitions/Mod Builder` : `Refresh`, `Validate All`, `Build`, `Build + Install`,
  `Build All`, `Open Output`, `Open ModsLocal`, `Add Dep` (copie une DLL tierce dans
  `Dependencies/` + l'ajoute aux `precompiledReferences`).
- Sortie : `<SDK>/Output/<ModId>/` = image exacte de ce qui part dans
  `…\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\<ModId>\` (DLL, `AssetBundles/<plateforme>/`,
  `Dependencies/`, `Locales/`, thumbnail).
- Override du chemin d'install : EditorPrefs `BAModBuilder.ModsLocalPath` (utile si le jeu est sur
  un autre disque/PC monté en réseau).
- Le validator bloque le build sur : manifest mal placé, ModId ≠ nom du dossier, ModId/asmdef non
  uniques, asmdef manquant ou dérivant de la liste canonique (quick-fix auto), pas de
  `[assembly: RegisterModClass]`, scoping AssetBundle incorrect, support macOS absent (le build
  fait les bundles Windows **et** Mac), `Enums.txt` mal formé, `Dependencies/`/`Locales/` mal formés.
- Entrées multiples par assembly OK : `[ModEntryOnInitializationLoad]`, `[ModEntryOnCityLoad]`,
  `[ModEntryOnMainMenuLoad]` — le squelette coop peut avoir une classe d'entrée réseau (city load)
  et une classe d'entrée dev-tools (main menu) séparées.

---

## 8. Setup recommandé pour CoopAmbitions

### 8.1 Arborescence cible

```
tres-big-ambition-/                      ← notre repo (source de vérité, open source)
  CoopAmbitions/                         ← le mod publié — lié par junction dans <SDK>/Assets/Mods/
    CoopAmbitions.asmdef                 (+ .meta commités, GUID stables)
    ModManifest.asset                    (créé une fois dans Unity, commité)
    thumbnail.png                        (≤1 Mo, requis pour l'upload Workshop)
    Locales/en.json, fr.json
    Dependencies/                        (HarmonyX quand on posera le premier patch)
    Scripts/
      Core/    CoopMod.cs (IModBigAmbitions), CoopRunner.cs, CoopVersion.cs
      Net/     NetMessage.cs, ITransport.cs, SteamTransport.cs, LoopbackTransport.cs, CoopSession.cs
      Sync/    LocalPlayerLocator.cs, RemotePlayerView.cs
      Interop/ Interop.Player.cs, Interop.Time.cs, Interop.Save.cs   ← SEUL endroit qui touche
                                                                        aux membres privés du jeu
      Config/  CoopSettings.cs, CoopOptionIds.cs, CoopOptionPersistence.cs,
               CoopOptionsUi.cs, DevConfig.cs (dev.json)
      Debug/   NetOverlay.cs (F8), CoopLog.cs (tags + WarnOnce + flag verbeux)
  CoopDevTools/                          ← mod compagnon LOCAL, jamais publié (2e junction, optionnelle)
    Dependencies/                        UnityExplorer.Standalone.Mono + UniverseLib + HarmonyX
    Scripts/DevToolsMod.cs               ExplorerStandalone.CreateInstance(), cheats dev
  tools/
    localconfig.sample.json              → copié en localconfig.json (GITIGNORÉ) :
                                           { sdkPath, unityEditorPath, modsLocalRoot?, secondPcDeployPath? }
    link-mod.ps1                         crée les junctions dans <SDK>/Assets/Mods/
    build.ps1                            build externe dotnet (adaptation du script Dudeldups) ; -Install
    run.ps1                              build -Install → steam://rungameid/1331550 → tail Player.log
    package.ps1                          zip GitHub Releases depuis <SDK>/Output/CoopAmbitions
    tail-log.ps1                         Get-Content -Wait du Player.log, filtre [Coop.*]
    fixtures/CoopTest.hsg                save de test (si le format le permet — sinon doc de création)
  docs/
    VERSIONS.md                          commit SDK, buildid jeu, version Unity/HarmonyX testés
    research/production/outillage-dev.md ← ce document
```

Le clone du SDK (`hovgaardgames/bigambitions`) reste **hors repo**, jetable ; on n'y commite rien.

### 8.2 Scripts à écrire (dans l'ordre de rentabilité)

1. **`tools/link-mod.ps1`** (10 lignes) : lit `localconfig.json`, fait
   `mklink /J "<sdk>\Assets\Mods\CoopAmbitions" "<repo>\CoopAmbitions"` (+ CoopDevTools), idempotent.
2. **`tools/build.ps1`** : adaptation directe de `BuildBigAmbitionsMods.ps1` de Dudeldups
   (déjà cloné dans `/home/user/dudeldups-mods/tools/external-build/` pour référence) :
   csproj net472 généré, refs = `GameDlls/*.dll` + `UnityEngine*` de l'éditeur + `Library/ScriptAssemblies`
   filtrées, defines Unity, `-Install` vers `ModsLocal` avec retry. Ajouter : copie du `.pdb`/`.mdb`
   en `-Configuration Debug`, et cible `-Deploy2ndPc` (copie vers `secondPcDeployPath`).
3. **`tools/run.ps1`** : `build.ps1 -Install` → si le jeu tourne déjà, juste un bip « toggle le mod » ;
   sinon `Start-Process "steam://rungameid/1331550"` → `tail-log.ps1`.
4. **`tools/tail-log.ps1`** : tail de `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\Player.log`
   avec surlignage de `[Coop.` et des exceptions.
5. **`tools/package.ps1`** (plus tard, phase release) : vérifie que le build vient du **Mod Builder**
   (pas du build externe), zippe `Output/CoopAmbitions` + README pour GitHub Releases.

### 8.3 Mise en route, étapes ordonnées

1. **Base Unity une fois** : cloner le SDK, ouvrir dans Unity 2022.3.62f2, Welcome window →
   Import DLLs from Steam ; noter commit SDK + buildid dans `docs/VERSIONS.md`.
2. `tools/link-mod.ps1` ; retour dans Unity (Ctrl+R) : le mod apparaît dans `Assets/Mods/` ;
   créer le `ModManifest.asset` (clic droit → Create), remplir ModId `CoopAmbitions`, asmdef,
   Locales ; **commiter l'asset + tous les `.meta`** depuis notre repo (ils vivent à travers la junction).
3. Premier **Build & Install via le Mod Builder** (build canonique : valide les 13 règles, installe
   l'arborescence complète dans `ModsLocal`). Corriger jusqu'au vert. Lancer le jeu, activer le mod,
   vérifier le log de chargement.
4. **Expérience n°1 — reload à chaud** (§1.5) : `build.ps1 -Install` jeu ouvert + toggle du mod.
   Documenter le résultat dans VERSIONS.md ; il fixe la boucle (toggle vs restart ; si restart :
   envisager le DevHost façon ScriptEngine plus tard, pas tout de suite).
5. Monter la **boucle quotidienne** : Rider ouvert sur les csproj générés par Unity (IntelliSense),
   itération via `run.ps1`, Unity ouvert seulement quand on touche manifest/locales/bundles ou pour
   le build canonique de fin de journée.
6. **Dev tools** : mod compagnon `CoopDevTools` avec UnityExplorer Standalone (§2.4) ; session
   d'exploration : vérifier `playerController`, `SaveGameManager.Current`, `DebugMode.dll`
   (auto-load de save ? cheats ?). En parallèle, installer dnSpyEx + le mono debug 2022.3.62f2 sur
   une **copie** du dossier du jeu (garder l'install Steam propre pour les tests « comme un joueur »).
7. **`dev.json` + auto-pilotage** : implémenter DevConfig (`autoLoadSave`, `autoHost`, `autoJoin`,
   `useLoopbackTransport`) + la save de test `CoopTest`. Objectif mesurable : **du `git commit` au
   « deux avatars qui bougent » en < 60 s sans toucher la souris dans les menus.**
8. **`LoopbackTransport`** avant d'attaquer la phase 2 (horloge) : tout le protocole se développe
   en solo, les sessions à deux comptes deviennent des sessions de validation.
9. **Overlay réseau F8** + `CoopLog` (tags, WarnOnce, verbosité pilotée par ModOptions) dès le MVP.
10. Phase release : `package.ps1`, GitHub Releases + upload Workshop depuis le jeu ; CI de
    compilation seulement si le besoin d'équipe apparaît (refs strippées privées, §3.2).

### 8.4 Les trois habitudes qui font la différence (synthèse des communautés)

1. **Le build installe, le jeu recharge** — jamais de copie manuelle, jamais de « quelle version
   est installée ? » (Jotunn, BepInEx, Dudeldups font tous ça).
2. **Un unload propre est un feature de dev** autant que de qualité : tout ce qui rend
   `OnUnloadAsync` complet rend aussi le hot reload et les reconnexions fiables.
3. **Chaque minute de friction se scripte** : les moddeurs prolifiques (Dudeldups : 15 mods) ne
   sont pas plus rapides à coder — leur boucle modifier→tester est juste 10× plus courte.
