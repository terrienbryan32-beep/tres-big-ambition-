# Internals de Big Ambitions — dossier de recherche pour le mod coop

> Recherche effectuée le 2026-08-31, sans accès aux DLL du jeu (elles s'importeront depuis l'installation Steam).
> Sources : repo SDK officiel de Hovgaard Games, repos de mods communautaires clonés localement, recherche web.
> Note d'accès : plusieurs domaines étaient bloqués par le proxy pendant la recherche (thunderstore.io, nexusmods.com, steamdb.info, store.steampowered.com, forum.bigambitionsgame.com, bigambitionsgame.com, fearlessrevolution.com, melonloader.net) — les infos correspondantes proviennent des extraits de moteur de recherche ; les liens restent valides pour consultation manuelle.

---

## 1. Fiche technique et fait crucial : le jeu est passé d'IL2CPP à Mono

- Jeu : **Big Ambitions** (Hovgaard Games), Steam appid **1331550**.
- Moteur : **Unity 2022.3.62f2** (version exigée par le SDK officiel — voir le README du repo officiel).
- Historique des versions récentes (via [SteamDB patchnotes](https://steamdb.info/app/1331550/patchnotes/) et extraits de recherche) :
  - EA 0.9 « The Struggle » — 30 septembre 2025
  - EA 0.10 (Cinéma/Théâtre, refonte usines, grosses optims) — 11 mars 2026
  - **EA 0.11 « The Workshop Awakens » (support officiel des mods + Steam Workshop) — juin 2026** ([annonce Steam](https://store.steampowered.com/news/app/1331550/view/679623809418921533))
  - **1.0 (sortie complète : The Hamptons, Personal Driver, Food Delivery, HQ Pricing Manager, Banking, minijeux) — 3 août 2026**
- **Backend scripting : Mono aujourd'hui, IL2CPP avant.** Faisceau de preuves :
  - Le SDK officiel importe les DLL du jeu depuis `Big Ambitions_Data/Managed` (constante `ManagedRelativePath = "Big Ambitions_Data/Managed"` dans `SteamInstallLocator.cs` du SDK) — un dossier `Managed` rempli de DLL du jeu n'existe que sur un build Mono.
  - Les mods officiels sont des **assemblies managées chargées à chaud** (`ModsLocal`, `OnLoadAsync`/`OnUnloadAsync`) — impossible sur IL2CPP sans interop.
  - Le fil [FearLess Cheat Engine](https://fearlessrevolution.com/viewtopic.php?t=23794) indique explicitement que la table a été « redesignée pour utiliser **Mono au lieu d'IL2CPP** » (donc bascule du jeu vers Mono, vraisemblablement autour de l'EA 0.11 pour permettre le modding officiel).
  - Les mods tiers plus anciens (trainer BepInEx 6 IL2CPP, [BAUI-Framework](https://www.nexusmods.com/bigambitions/mods/6), [ZGD_BA Framework](https://www.nexusmods.com/bigambitions/mods/12), article [melonloader.net](https://melonloader.net/modding-big-ambitions-with-melonloader/)) datent de l'ère IL2CPP : leur documentation parle d'interop IL2CPP. **Ne pas se fier à ces docs pour l'état actuel** ; elles restent utiles pour les noms de classes.
  - Conséquence pour le coop : **Harmony fonctionne nativement** (Mono), dnSpy/ILSpy décompilent directement les DLL de `Managed`, et on peut même s'appuyer sur le chargeur de mods officiel plutôt que MelonLoader/BepInEx.

---

## 2. Code source public : ce que les mods existants révèlent des internals

### 2.1 Repos clonés localement

| Repo | Contenu | Clone local |
|---|---|---|
| [hovgaardgames/bigambitions](https://github.com/hovgaardgames/bigambitions) | **SDK officiel de modding** : projet Unity, outillage d'import des DLL, Mod Builder, 5 mods d'exemple (BusinessType, Furniture, Options, Vehicle, BackAlleyDealer) | `/home/user/ba-official` |
| [Dudeldups/big-ambitions-mods](https://github.com/Dudeldups/big-ambitions-mods) | Fork enrichi du SDK avec **~15 mods complets** (BigHax, Pink, Taxi!, HQCentral, StreetQuestRPG, VehicleRuntimeTuner, CameraTools, Gun Store, SharedWholesaleDesk, PetShop, AudiRS6R…) — la meilleure mine d'internals | `/home/user/ba-dudeldups` |
| [Dudeldups/big-ambitions-tools](https://github.com/Dudeldups/big-ambitions-tools) | Site compagnon Next.js ([big-ambitions-tools.com](https://big-ambitions-tools.com)) avec base de données du jeu par version (`data/game/0.10`, `0.11`, `1.0`) — utile pour les données économiques, pas pour le code | `/home/user/ba-tools` |

Autres mods notables (source non publiée trouvée) : [Big Ambitions Trainer](https://www.nexusmods.com/bigambitions/mods/1) (menu INSERT : argent, stats, temps/âge, véhicules, téléportation — [fil forum](https://forum.bigambitionsgame.com/t/mod-big-ambitions-in-game-trainer/7271)), [Aluna's Trainer](https://www.nexusmods.com/bigambitions/mods/2), [BAUI-Framework](https://www.nexusmods.com/bigambitions/mods/6) (génération procédurale d'UI + apps de téléphone custom), [ZGD_BA Framework](https://www.nexusmods.com/bigambitions/mods/12) (événements C# économie/business/UI, ère BepInEx-IL2CPP), [Fuel Mod sur ModDB](https://www.moddb.com/downloads/big-ambitions-fuel-mod).

### 2.2 L'API officielle BAModAPI (assembly `BigAmbitions.ModAPI.dll`)

Cycle de vie d'un mod (observé dans tous les exemples) :

```csharp
[assembly: RegisterModClass(typeof(MonMod))]

[ModEntryOnInitializationLoad]          // chargé à l'init du jeu
public class MonMod : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => new[] { "AssetBundles/mon-mod.unity3d" };
    public Task OnLoadAsync(ModContext context) { ... }   // context.ModId, context.Logger
    public Task OnUnloadAsync() { ... }                   // déchargement à chaud !
}
```

Services exposés par `BAModAPI` / `BAModAPI.Services` (usages observés) :
- `AssetService.GetBundle(modId, bundleKey)`, `AssetService.Spawn(...)` — AssetBundles du mod.
- `OptionsService.Register(...)` / `RemoveModOptions(...)` — options de mod dans le menu natif.
- `ModdingAPI.RegisterModBusinessType(...)`, `RegisterModVehicleType(...)` (+ Unregister) — contenu custom.
- `ItemsGetter.RegisterModItem(item)` / `UnregisterModItem(name)` / `GetByName` / `AllItems` / `IsModItem` (namespace `BigAmbitions.Items`).
- `ContractItemsForSaleService` : `SetItemsForContact`, `SetVehiclesForContact`, `SetContactForAddress`, `RemoveContact…` — vendre des items via un contact.
- `CallDialogFactory.RegisterDialog(dialogType, () => new MonDialog())` — dialogues téléphoniques custom (namespace `Dialogs`).
- **`GlobalEvents`** — le hub d'événements le plus précieux pour le coop :
  - `GlobalEvents.RegisterOnGameLoadedCallback` / `RegisterOnGameLoadedLateCallback`
  - `GlobalEvents.onNewDay`, `GlobalEvents.onNewHour`
  - `GlobalEvents.onEnterBuilding`, `onEnterBuildingDelayed`
  - `GlobalEvents.onVehicleVariablesChanged`

### 2.3 Singletons et services du jeu utilisés par les mods

Beaucoup de types du jeu sont dans le **namespace global** (pas de préfixe `BigAmbitions.`) :

- **`GameManager`** (global) : `GameManager.Instance`, `GameManager.Instance.playerController` (le joueur local !), et surtout **`GameManager.ChangeMoneySafe(montant, TransactionInfo, showNotification)`** — LA méthode de mutation d'argent (utilisée par BackAlleyDealer pour débiter un achat de véhicule).
- **`SaveGameManager`** (`BigAmbitions.SaveSystem.Legacy`) :
  - `SaveGameManager.Current` — l'objet SaveGame vivant (état complet de la partie en mémoire).
  - `SaveGameManager.MarkChange()` — marque l'état dirty (appelé après chaque mutation).
  - `SaveGameManager.Save(SaveGameManager.SaveType.Default, null, null)` — sauvegarde programmatique.
- Champs de `SaveGameManager.Current` observés dans les mods : **`Money`**, `Day`, `Hour`, `Minute`, `VehicleInstances`, `EmployeeInstances`, `Transactions`, `Contacts`, `DeliveryContracts`, `FurnitureDeliveryContracts`, `BuildingRegistrations`, `hasEverUsedMods`, et **`modData`** (un `Dictionary<string, string>` persistant par sauvegarde — les mods y stockent leur état sérialisé ; canal de persistance idéal pour l'état coop).
- **`BuildingManager.Instance`** : `.cityBuildingController.customPositions` (points de spawn).
- **`CityManager`** : `CityManager.LoadIndoors(...)` (chargement des intérieurs).
- Transactions : `new TransactionInfo(LegacyRef.Transaction.VehicleBought, data, taxDeductible)` — enum `LegacyRef.Transaction` (types de transaction) et `LegacyRef.MessageType` (messages de dialogue).
- Véhicules : `VehicleInstance` (champs `id`, `vehicleColorName`, `fuel`), `VehicleType` (`maxFuel`, `taxDeductible`, `vehicleTypeName`), `VehicleTypeHelper`, `VehicleHelper.CreateAndSpawnVehicle`, `VehicleHelper.AllPlayerVehicles`, `VehicleHelper.TeleportVehicleToGround`, `VehicleParkingHelper.TryGetRandomParkingGarageSpot`, `UuidHelper.GenerateBase64Uuid()` (ids en base64).
- Temps : struct **`BigAmbitions.DayNightCycle.Timestamp`** avec champs `Day` (int), `Hour` (int), `Minute` (float) — clonée par réflexion dans BigHax.
- Types atteints par réflexion (`FindType`) dans les mods de Dudeldups — donc noms exacts confirmés à l'exécution : `BuildingManager`, `CityManager`, `CityMap`, `CtaManager`, `BaseHuman`, `ThirdPersonCharacter`, `AppearanceSetter`, `Entities.Homeless`, `ItemController`, `Contact`, `Order`, `SellerStandController`, `NavigationBlocker`, `AI.Customers.CustomerEntries.CustomerEntry` + `CustomerEntriesHelper`, `Buildings.Indoors.WallsVisibilityHelper`, `BigAmbitions.InteriorDesigner.WallsVisibility`, `UI.Dialog.DialogUI`, `UI.MiniMenu.MiniMenu`, `UI.Smartphone.FullMenu`, `GetType("BigAmbitions.Factories.Recipes.RecipeItem")`.
- Le mod « Pink » cherche le joueur via les chemins de GameObjects `"GameManager/PlayerController"` et les membres `PlayerController` / `playerController` / `CurrentPlayerController` — confirme que le contrôleur joueur est un enfant/champ de `GameManager`.
- Autres namespaces du jeu importés par les mods : `Buildings`, `Entities`, `Dialogs`, `Helpers`, `Streets`, `Extensions`, `Services`, `Localizor` (localisation), `Vehicles.VehicleTypes`, `UI.Notification`, `UI.Dialog`, `UI.Smartphone.Apps.Contacts`, `Player.HUD.ItemInfoOverlays`, `BigAmbitions.Characters` (avec `CharacterDefinition`, `CharacterCatalog`, `CharacterId`, scheduling, presentation…).

---

## 3. Cartographie des DLL du jeu (`Big Ambitions_Data/Managed`)

Liste canonique exacte (fichier `CanonicalGameDlls.cs` du SDK officiel — c'est la liste que le Mod Builder importe) :

| DLL | Couverture probable / confirmée |
|---|---|
| `BigAmbitions.dll` | Cœur du jeu : GameManager, SaveGameManager (`BigAmbitions.SaveSystem.Legacy`), économie, buildings, dialogues, UI, véhicules… (les namespaces globaux `Buildings`, `Entities`, `Helpers`, `Dialogs`, `UI.*` vivent probablement ici) |
| `BigAmbitions.Characters.dll` | Personnages : `CharacterDefinition`, `CharacterCatalog`, schedules, apparence, spawn/teleport |
| `BigAmbitions.AI.dll` | IA : clients (`AI.Customers.CustomerEntries.*`), trafic, comportements (couplé à BehaviorDesigner) |
| `BigAmbitions.Items.dll` | Items : `Item`, `ItemsGetter`, enregistrement d'items moddés |
| `BigAmbitions.Factories.dll` | Usines/production : `BigAmbitions.Factories.Recipes.RecipeItem` |
| `BigAmbitions.InteriorDesigner.dll` | Mode design d'intérieur : `WallsVisibility`, placement de meubles |
| `BigAmbitions.PlacementSystem.dll` | Système de placement/grille (posé d'objets) |
| `BigAmbitions.Neighborhoods.dll` | Quartiers de la ville (zonage, adresses) |
| `BigAmbitions.Seasons.dll` | Saisons/météo |
| `BigAmbitions.InputSystem.dll` | Wrapper Input System (rebinding) |
| `BigAmbitions.SoundSystem.dll` | Audio |
| `BigAmbitions.DebugMode.dll` | **Console/outils debug internes — à décompiler en priorité, souvent plein de commandes utiles (spawn, argent, temps)** |
| `BigAmbitions.GameAnalytics.dll` | Télémétrie GameAnalytics |
| `BigAmbitions.ModAPI.dll` | **BAModAPI** : `IModBigAmbitions`, `ModContext`, `GlobalEvents`, services (surface publique « stable » pour mods) |
| `BigAmbitions.ModsInternal.dll` | Chargeur de mods interne : découverte `ModsLocal`/Workshop, chargement des assemblies, upload |
| `BigAmbitions.Legacy.dll` | Anciennes classes (ex. `BigAmbitions.Legacy.PlayerPref` qui collisionne avec `PlayerPrefs` Unity — cité dans le SDK), `LegacyRef.*` |
| `DayNightCycle.dll` | Temps de jeu : `BigAmbitions.DayNightCycle.Timestamp {Day, Hour, Minute}`, tick horaire/journalier → alimente `onNewHour`/`onNewDay` |
| `HGExtensions.dll` / `HGPlugins.dll` | Utilitaires maison Hovgaard Games (extensions C#, helpers) |
| `OdinSerializer.dll` | **Sérialisation binaire des sauvegardes** (voir §4) |
| `Google.Protobuf.dll` | Protobuf — probablement pour la télémétrie et/ou une partie du format de save/cloud |
| `Google.OrTools.dll` | Solveur d'optimisation Google (probable : pathfinding logistique, planification livraisons/employés) |
| `Facepunch.Steamworks.Win64.dll` | **Steamworks C#** : Workshop (upload des mods), cloud saves, achievements — et surtout **Steam Networking Sockets/Lobbies déjà embarqués : réutilisable pour le transport réseau du coop sans dépendance supplémentaire** |
| `BehaviorDesigner.Runtime.dll` | Arbres de comportement (IA NPC) |
| `DOTween.dll` + `DOTween.Modules.dll` | Tweening (animations UI/objets) |
| `HBAO.HighDefinition.Runtime.dll` | Ambient occlusion HDRP (le jeu est en HDRP) |
| `JimmysUnityUtilities.dll`, `UnityUIExtensions.dll`, `NaughtyAttributes.Core.dll`, `ExternalPlugins.dll`, `System.Runtime.CompilerServices.Unsafe.dll` | Libs utilitaires tierces |

Détails d'import utiles (SDK officiel) : les DLL sont copiées dans `Assets/_BaDependencies/GameDlls/` avec `isExplicitlyReferenced: 1` (pas d'auto-référence, sinon collisions de types), le define `BA_GAME_DLLS_IMPORTED` est activé, et le **buildid Steam est tracké pour signaler « game updated — re-import »** : le SDK lui-même acte que chaque MAJ Steam invalide potentiellement les références.

### Ce qu'implique OdinSerializer + Protobuf

- **OdinSerializer** est un sérialiseur .NET open source (Sirenix) qui gère des graphes d'objets C# complets en binaire/JSON/nodes, avec références Unity. Sa présence + le namespace `BigAmbitions.SaveSystem.Legacy` + des fichiers de save binaires suggèrent fortement que **le `SaveGame` est un gros graphe d'objets C# sérialisé par Odin en binaire**. Bonne nouvelle pour le coop : si l'état du jeu est un graphe sérialisable Odin, on peut **sérialiser des sous-arbres (delta d'état) avec le même OdinSerializer pour la synchro réseau** et pour le transfert de la partie à l'invité au join.
- Attention au mot « Legacy » : il existe peut-être un système de save plus récent à côté (à vérifier dans dnSpy : chercher un namespace `BigAmbitions.SaveSystem` non-Legacy) ; mais tous les mods de 2026 passent encore par `SaveGameManager` Legacy.
- **Google.Protobuf** : soit pour GameAnalytics, soit pour un sous-format (métadonnées de save cloud, échanges Workshop). À confirmer en cherchant les types générés `*.pb` / classes héritant de `IMessage` dans dnSpy.

---

## 4. Sauvegardes

- **Emplacement** : `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\SaveGames\`
  ([DigiStatement](https://digistatement.com/big-ambitions-save-file-location-where-is-it/), [games-manuals](https://games-manuals.com/save-game-location-backup-installation/big-ambitions-1331550), [discussion Steam](https://steamcommunity.com/app/1331550/discussions/0/3823034248717655203/)). Sur macOS : équivalent dans `~/Library/Application Support` (à vérifier).
- Chaque partie a un **sous-dossier dont le nom est une chaîne base64 se terminant par `==`** (id encodé — cohérent avec `UuidHelper.GenerateBase64Uuid()` vu dans le code).
- **Format** : fichiers **`.hsg`** (« Hovgaard Save Game »), **binaires** (très probablement OdinSerializer binaire, cf. §3). Le dossier de save contient aussi des **fichiers image des logos d'entreprises** (PNG nommés d'après les sociétés) que des joueurs remplacent à la main ([discussion Steam Business Signs](https://steamcommunity.com/app/1331550/discussions/0/6620894968760967926/)).
- **Steam Cloud est actif par défaut** → pour les tests coop, penser à le désactiver sur un profil de test (sinon les saves de test se synchronisent).
- **Éditeurs de save communautaires : il n'en existe pas de mature.** [savegame.info](https://savegame.info/big-ambitions/) confirme « no save-editor found » ; la communauté passe par :
  - la **table Cheat Engine** de FearLess Revolution ([fil principal](https://fearlessrevolution.com/viewtopic.php?t=23794), ~36 cheats, MAJ mars 2026, réécrite pour Mono) — édition en mémoire, pas du fichier ;
  - les **trainers-mods** (Nexus mods 1 et 2) qui modifient l'état via le code du jeu ;
  - le [fil forum « How to modify a save file »](https://forum.bigambitionsgame.com/t/how-to-modify-a-save-file-to-change-the-difficulty/2305) (non consultable via le proxy — à lire manuellement) ;
  - des saves toutes faites à télécharger ([savegamedownload.com](https://savegamedownload.com/big-ambitions-easy-start-savegame/)).
- **Pour le coop** : la voie propre n'est PAS d'éditer les `.hsg`, mais de manipuler `SaveGameManager.Current` en mémoire + `MarkChange()`/`Save()` ; et de persister l'état coop dans `SaveGameManager.Current.modData["coop:..."]` (mécanisme officiel, survit aux saves/loads, voyage avec le fichier de save).

---

## 5. Workflow de rétro-ingénierie (légal : analyse locale, pas de redistribution des DLL)

1. **Décompilation statique — dnSpy ou ILSpy** :
   - Ouvrir `...\steamapps\common\Big Ambitions\Big Ambitions_Data\Managed\BigAmbitions.dll` (et les autres DLL `BigAmbitions.*`, `DayNightCycle.dll`). Comme le jeu est **Mono**, la décompilation donne du C# quasi complet avec noms d'origine — pas besoin d'Il2CppDumper/Cpp2IL.
   - dnSpy permet en plus : « Analyze » (qui lit/écrit un champ, qui appelle une méthode), pose de **breakpoints sur le jeu en cours** (attacher dnSpy au process Unity Mono via le debug engine), et édition IL à chaud pour des tests rapides.
   - Astuce : commencer par `GameManager`, puis suivre `ChangeMoneySafe` et `SaveGameManager.Current` avec « Analyze → Used By » pour cartographier tous les points de mutation d'état.
2. **Exploration runtime — UnityExplorer** :
   - Utiliser une build **Mono** d'UnityExplorer (`UnityExplorer.MelonLoader.Mono.zip` ou `UnityExplorer.BepInEx5.Mono.zip`, ou la version standalone injectable). Repo d'origine : github.com/sinai-dev/UnityExplorer (archivé) ; fork maintenu : github.com/yukieiji/UnityExplorer.
   - Installer MelonLoader ≥ 0.6.x via l'installeur pointé sur `Big Ambitions.exe` (le [Fuel Mod ModDB](https://www.moddb.com/downloads/big-ambitions-fuel-mod) confirme MelonLoader 0.6.6+ fonctionnel ; [MelonLoader 0.7.0 est packagé pour BA sur Thunderstore](https://thunderstore.io/c/big-ambitions/p/LavaGang/MelonLoader/v/0.7.0/)), déposer `UnityExplorer.dll` dans `Mods\`, F7 en jeu.
   - En live : `Object Explorer → Search` sur `GameManager` → inspecter `Instance.playerController` (position, composants) ; chercher le singleton du temps (taper `Timestamp`, `DayNight`) ; le C# REPL intégré permet de tester `GameManager.ChangeMoneySafe(...)` immédiatement.
3. **Voie « SDK officiel » (recommandée en parallèle)** : cloner [hovgaardgames/bigambitions](https://github.com/hovgaardgames/bigambitions), ouvrir dans Unity 2022.3.62f2, laisser la Welcome Window importer les DLL — on obtient **IntelliSense complet sur toute l'API du jeu dans Rider/VS**, ce qui est souvent plus rapide que dnSpy pour explorer les signatures. Un mod de debug jetable (pattern `BigHaxRuntime` : `MonoBehaviour` + `DontDestroyOnLoad` + hotkeys) sert de banc d'essai rechargeable à chaud (`OnUnloadAsync`).
4. **Cheat Engine (complément)** : la [table FearLess](https://fearlessrevolution.com/viewtopic.php?t=23794) utilise les features **Mono** de CE (`Mono → Activate mono features`, dissect des classes) — utile pour retrouver des instances vivantes et valider des offsets.
5. **Harmony** : inclus avec MelonLoader/BepInEx (0Harmony). Sur Mono, patcher `Prefix/Postfix` sur `GameManager.ChangeMoneySafe`, le tick de `DayNightCycle`, et les setters de `SaveGameManager` est le moyen le plus sûr d'intercepter les mutations d'état pour les répliquer en réseau. Attention : le chargeur officiel ne charge pas Harmony pour vous — l'embarquer comme dépendance du mod (dossier `Dependencies/` du manifest) ou rester sur MelonLoader.
6. **Cadre légal** : analyse/modding local d'une copie possédée = pratique tolérée et ici **encouragée par l'éditeur** (SDK public, licence du repo officiel). Ne jamais redistribuer les DLL du jeu ni de contenu extrait ; le SDK gitignore d'ailleurs les DLL importées ; distribuer uniquement le code du mod.

---

## 6. Historique du modding officiel et position sur le multijoueur

- **EA 0.11 « The Workshop Awakens » (juin 2026)** : support officiel des mods + **intégration Steam Workshop** ([annonce](https://store.steampowered.com/news/app/1331550/view/679623809418921533), [Workshop](https://steamcommunity.com/app/1331550/workshop/), [vidéo](https://www.youtube.com/watch?v=hBhyxlt65kE)). Le flux officiel : SDK Unity → Mod Builder → `ModsLocal` → menu « Mods → Mod Creator » en jeu → upload Workshop.
- **Ce que l'API officielle permet** (constaté dans le SDK) : items/meubles, véhicules, types de business, contacts+dialogues téléphoniques, options, AssetBundles, localisation (Localizor + `Locales/`), dépendances DLL tierces, `modData` persistant. **Pas de sandbox** : le mod est une assembly .NET arbitraire avec accès à tout le runtime — rien n'interdit techniquement le réseau, les patches Harmony, etc. Les « limites » sont plutôt : API non documentée hors exemples, et re-build/re-import à chaque MAJ du jeu.
- **Multijoueur — position de Jonas Hovgaard** ([discussion Steam](https://steamcommunity.com/app/1331550/discussions/0/612031852355541814/), [autre fil](https://steamcommunity.com/app/1331550/discussions/0/3827536762649311520/)) :
  - 2023 : « massive undertaking », 4-6 mois de retard de contenu, réaliste à 1-2 ans.
  - 2025 : **le multijoueur ne sera PAS ajouté à Big Ambitions 1** (réécrire le code existant prendrait trop de temps vs contenu/end-game) ; il sera « definitely » dans le prochain jeu/la suite.
  - → Un mod coop ne marche sur les plates-bandes d'aucune feature officielle prévue pour BA1 ; et le code n'a **pas** été écrit pour le multi (aucune séparation client/serveur : tout l'état vit dans `SaveGameManager.Current` + singletons — architecture host-authoritative à construire soi-même).
- **Rythme des mises à jour** (risque de casse des mods) : grosses versions tous les ~3 mois (0.9 sept. 2025 → 0.10 mars 2026 → 0.11 juin 2026 → 1.0 août 2026) avec de **nombreux hotfixes/builds entre chaque** ([SteamDB patchnotes](https://steamdb.info/app/1331550/patchnotes/) liste des dizaines de builds par version). Le SDK trace le buildid Steam et demande un ré-import des DLL après chaque MAJ. Prévoir : CI qui re-build le mod contre les DLL fraîches + accès maximal via réflexion/`FindType` (pattern déjà utilisé par BigHax justement pour survivre aux MAJ). Post-1.0 (depuis août 2026), le rythme devrait se calmer mais des patches 1.0.x tombent encore.

---

## 7. Communauté — où poser des questions techniques

- **Discord officiel Hovgaard Games** : https://discord.com/invite/hovgaardgames — possède des **canaux modding** (confirmé par [ce fil Steam](https://steamcommunity.com/app/1331550/discussions/0/5440953210416893939/)). Meilleur endroit pour les questions d'API.
- **Forum officiel** : https://forum.bigambitionsgame.com/ avec une [catégorie Mods dédiée](https://forum.bigambitionsgame.com/c/mods/26) (les auteurs de trainers y publient).
- **GitHub officiel** : issues/PRs sur [hovgaardgames/bigambitions](https://github.com/hovgaardgames/bigambitions).
- **Nexus Mods** : https://www.nexusmods.com/games/bigambitions (+ [article d'installation](https://www.nexusmods.com/bigambitions/articles/1)).
- **Thunderstore** : https://thunderstore.io/c/big-ambitions/ (« Big Ambitions Mod Database », distribution MelonLoader).
- **ModDB** : https://www.moddb.com/games/big-ambitions/downloads.
- **Wiki Fandom** : https://big-ambitions.fandom.com/ ; **fansite** [biggerambitions.com](https://www.biggerambitions.com/) (page mods) ; **outils** [big-ambitions-tools.com](https://big-ambitions-tools.com) (données du jeu par version).
- Auteur communautaire clé : **Dudeldups** (repos GitHub ci-dessus, très actif, code propre — bon contact).

---

## Ce qu'il faut vérifier en premier dans dnSpy

Ordre de priorité pour le coop, avec quoi chercher et pourquoi :

1. **`GameManager`** (namespace global, `BigAmbitions.dll`) — le hub. Vérifier : `Instance`, `playerController` (type exact du contrôleur joueur : champ ? propriété ? enfant `GameManager/PlayerController` ?), **`ChangeMoneySafe(float/decimal, TransactionInfo, bool)`** et toutes les autres méthodes `ChangeMoney*`, l'`Awake`/ordre d'initialisation. C'est la cible n°1 des patches Harmony.
2. **`PlayerController` / `ThirdPersonCharacter`** — mouvement, input, interactions. Identifier : comment il est spawné, ce qui le lie à `GameManager`, s'il est instanciable une 2e fois (base du joueur distant), et `AppearanceSetter`/`BaseHuman` pour l'apparence de l'avatar invité.
3. **`SaveGameManager` + classe `SaveGame`** (`BigAmbitions.SaveSystem.Legacy`) — inventorier TOUS les champs de `SaveGameManager.Current` (Money, Day/Hour/Minute, VehicleInstances, EmployeeInstances, Businesses?, Transactions, Contacts, BuildingRegistrations, modData…) : c'est la liste exhaustive de l'état à synchroniser. Vérifier comment `Save()` sérialise (appels OdinSerializer ? format `.hsg` ? compression ?) et si un `SaveSystem` non-Legacy existe.
4. **`DayNightCycle` / `Timestamp`** (`DayNightCycle.dll`) — trouver le singleton du temps (qui incrémente `Day/Hour/Minute`), la vitesse du temps, la pause, et d'où partent `GlobalEvents.onNewHour`/`onNewDay`. Le temps est la première chose à asservir au host en coop.
5. **`GlobalEvents`** (`BigAmbitions.ModAPI.dll`) — lister TOUS les événements/callbacks disponibles (il y en a sûrement plus que les 7 observés) : chaque event est un point de réplication gratuit.
6. **`TransactionInfo` + `LegacyRef.Transaction`** — l'enum complète des transactions = taxonomie de tous les flux d'argent du jeu (à répliquer ou à router vers le « portefeuille » commun du coop).
7. **`BigAmbitions.DebugMode.dll`** en entier — les commandes de debug internes (spawn, set money, set time, teleport) sont des raccourcis tout faits pour piloter l'état depuis le code réseau.
8. **`VehicleHelper` / `VehicleInstance` / `VehicleController`** — spawn/teleport/état des véhicules déjà exposés par des helpers statiques : premier système « facile » à synchroniser après le joueur.
9. **`BuildingManager` / `CityManager` / chargement des intérieurs** (`CityManager.LoadIndoors`) — comprendre le streaming intérieur/extérieur : en coop, que se passe-t-il quand deux joueurs sont dans des bâtiments différents ? (probable : un seul intérieur chargé → grosse contrainte de design).
10. **`Facepunch.Steamworks`** (usage réel dans `BigAmbitions.dll`) — vérifier quelles features Steamworks sont initialisées (SteamClient, Workshop, Cloud) et si `SteamNetworkingSockets`/lobbies sont utilisables sans conflit pour le transport réseau du mod.
11. **`BigAmbitions.ModsInternal.dll`** — comment les mods sont découverts/chargés/déchargés (ordre, domaine, isolation) : détermine si le mod coop peut se recharger à chaud et comment embarquer Harmony/ses dépendances.
12. **`AI.Customers.*` + `BehaviorDesigner`** — le spawn des clients/NPC (`CustomerEntry.SpawnTime` est un `Timestamp`) : décider tôt si les NPC sont simulés côté host uniquement (recommandé) et ce qu'il faut geler côté client.
