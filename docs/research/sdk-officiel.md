# Dissection du SDK de modding officiel Big Ambitions (Hovgaard Games)

> Source analysée : clone local `/home/user/hovgaardgames/bigambitions` (repo GitHub `hovgaardgames/bigambitions`).
> Unity **2022.3.62f2**, HDRP 14.0.12, Input System 1.7.0, Newtonsoft.Json 3.2.1 (`Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`).
> Tous les chemins ci-dessous sont relatifs à la racine du SDK sauf mention contraire.

## Vue d'ensemble du dépôt

```
Assets/
  Editor/
    Bootstrap/        SteamInstallLocator.cs, GameDllImporter.cs, CanonicalGameDlls.cs
    Branding/         WelcomeWindow.cs (+ background.png, balogo.png)
    ModBuilder/       ModBuilderWindow, ModPackager, ModValidator, ModDiscovery,
                      ModInstaller, DependencyActions, AsmdefFile, BAModManifest(+Editor)
    ItemControllerTools/  ItemControllerAutoSetupEditor.cs
  Mods/
    BackAlleyDealer/        (exemple le plus complet : contact, dialogue, enums, 2 locales)
    Example-BusinessType/   (nouveau type de commerce + item)
    Example-Furniture/      (nouvel item/meuble)
    Example-Options/        (options de mod dans le menu — "SliderInMyDMs")
    Example-Vehicle/        (nouveau véhicule)
  ScriptTemplates/    81-C# MyCityModScript.cs.txt, 81-C# MyMenuModScript.cs.txt
  _BaDependencies/
    GameDlls/         (rempli par l'importeur — DLL + .meta gitignorés)
    Input System/1.7.0/Rebinding UI/   (utilitaires Unity sans rapport avec l'API mod)
```

Le code éditeur du SDK vit dans le namespace `BAModTemplate.Editor` ; l'API runtime exposée par le jeu vit dans `BAModAPI` (DLL `BigAmbitions.ModAPI.dll`, non fournie dans le repo — importée depuis l'installation Steam).

---

## 1. Cycle de vie d'un mod

### 1.1 Attributs (namespace `BAModAPI`)

**Enregistrement (niveau assembly)** — obligatoire, sinon rejet par le validateur :

```csharp
[assembly: RegisterModClass(typeof(BackAlleyDealerInit))]
```

- Type complet : `BAModAPI.RegisterModClassAttribute` (constante `RegisterModClassAttributeFullName` dans `ModValidator.cs:40`).
- Expose une propriété `ModClassType` (de type `Type`) — le validateur la lit par réflexion (`ModValidator.cs:256`).
- **Plusieurs `RegisterModClass` par assembly sont autorisés** : `Example-BusinessType` en déclare deux (`ExampleBusinessTypeMod` + `ExampleBusinessTypeCityMod`), BackAlleyDealer aussi (`BackAlleyDealerInit` + `BackAlleyDealerCity`). C'est le mécanisme pour avoir plusieurs points d'entrée à des moments de chargement différents.

**Attributs de point d'entrée (niveau classe)** — observés dans le SDK :

| Attribut | Moment de chargement | Où on le voit |
|---|---|---|
| `[ModEntryOnInitializationLoad]` | à l'initialisation du jeu (données, registres d'items…) | BackAlleyDealerInit, ExampleBusinessTypeMod, ExampleFurnitureMod, ExampleOptionsMod, ExampleVehicleMod |
| `[ModEntryOnCityLoad]` | au chargement de la ville (partie chargée, monde présent) | BackAlleyDealerCity, ExampleBusinessTypeCityMod, template `81-C# MyCityModScript.cs.txt` |
| `[ModEntryOnMainMenuLoad]` | au menu principal | template `81-C# MyMenuModScript.cs.txt` (aucun exemple compilé) |

**Y en a-t-il d'autres ?** Le validateur (`ModValidator.cs:271-273`) accepte tout attribut dont le nom commence par `ModEntryOn` **ou** `ModEntryMain`, et son commentaire dit : *« Runtime uses ModEntryOn\* (city, init, **intro**, …) and ModEntryMainMenu for main menu »*. Il existe donc très probablement au minimum un `ModEntryOnIntroLoad` (ou similaire) dans `BigAmbitions.ModAPI.dll`, mais seuls les trois attributs ci-dessus sont attestés dans les sources du SDK. Sans attribut `ModEntryOn*`, le validateur émet un **Warning** : *« it will never be loaded by the runtime »*.

### 1.2 Interface `BAModAPI.IModBigAmbitions`

Surface complète telle qu'implémentée par les 5 exemples et les 2 templates :

```csharp
public interface IModBigAmbitions
{
    string[] RelativeAssetBundlePaths { get; }   // ex. { "AssetBundles/example-furniture.unity3d" } ; Array.Empty<string>() si aucun bundle
    Task OnLoadAsync(ModContext context);        // appelé au moment défini par l'attribut ModEntryOn*
    Task OnUnloadAsync();                        // appelé au déchargement — le mod DOIT défaire ce qu'il a fait
}
```

Points importants observés :
- Le validateur instancie la classe via `Activator.CreateInstance` (constructeur **sans paramètre** requis) puis lit `RelativeAssetBundlePaths` (`ModValidator.cs:337-355`).
- Tous les exemples retournent `Task.CompletedTask` — l'API est async mais les usages sont synchrones.
- La symétrie load/unload est prise très au sérieux dans les exemples : dé-enregistrement des items/véhicules/business types, restauration des tableaux patchés (`ExampleBusinessTypeCityMod.RestoreShowcaseShelves`), retrait des options (`OptionsService.RemoveModOptions`), retrait des contacts (`ContractItemsForSaleService.RemoveContact`). Le jeu peut donc décharger/recharger un mod à chaud (activer/désactiver depuis le menu Mods).

### 1.3 `BAModAPI.ModContext`

Tout ce qui est **effectivement exposé/utilisé** dans le SDK :

| Membre | Type observé | Usage |
|---|---|---|
| `ModId` | `string` | passé à `AssetService.GetBundle(context.ModId, key)` et `OptionsService.Register(context.ModId, options)` — c'est l'identité du mod côté runtime |
| `Logger` | objet avec au moins `Info(string)` | `context.Logger.Info("Contact created")`, etc. |

Rien d'autre n'est touché par aucun exemple. Les mods stockent le contexte dans un champ (`private ModContext _context;`) pour l'utiliser plus tard (log, ModId). Aucun exemple n'utilise de `Logger.Warn/Error` — leur existence est probable mais non attestée.

### 1.4 Autres symboles `BAModAPI` observés

- `BAModAPI.Services.AssetService.GetBundle(string modId, string relativeBundlePath)` → `UnityEngine.AssetBundle` (voir §7).
- `ModEnumHash.GetSafeHash(string name)` → valeur entière stable, castée vers un enum du jeu :
  ```csharp
  var dialogType = (CallDialogType)ModEnumHash.GetSafeHash("backalleydealer_calldialogtype");
  ```
  C'est le mécanisme officiel pour **étendre un enum du jeu** sans collision (couplé à `Enums.txt`, voir §3.4). Import visible : `using BAModAPI;` — donc `ModEnumHash` est dans `BAModAPI`.
- `ModdingAPI.RegisterModBusinessType / UnregisterModBusinessType / RegisterModVehicleType / UnregisterModVehicleType` (voir §5). Les fichiers qui l'appellent importent `BAModAPI` et `BAModAPI.Services` — la classe est dans l'un des deux (non déterminable sans la DLL).
- `ModOptions` / `OptionsService` : le fichier `ExampleOptionsLogic.cs` importe `BAModAPI` **et** `BigAmbitions.Mods` — ces deux types sont donc dans l'un de ces deux namespaces (probablement `BigAmbitions.Mods`, sinon l'import serait inutile).

---

## 2. Pipeline Mod Builder

### 2.1 `ModBuilderWindow` (`Assets/Editor/ModBuilder/ModBuilderWindow.cs`)

- Fenêtre IMGUI, menu **`Big Ambitions/Mod Builder`** (`[MenuItem]`, priorité 10).
- **Verrouillée** tant que `GameDllImporter.GetStatus()` n'est pas `UpToDate` ou `UpdateAvailable` (elle renvoie vers la Welcome window sinon).
- Boutons : `Refresh` / `Validate All` (relance `ModDiscovery.DiscoverAll()` + `ModValidator.Validate`), `Build All`, `Build + Install All`, `Open Output`, `Open ModsLocal`, et par mod : `Build`, `Build + Install`, `Add Dep` (→ `DependencyActions.AddDependencyViaFilePicker`), `Reveal`.
- **Un mod avec au moins une erreur de validation ne peut pas être construit** (`canBuild = !ModPackager.IsBusy && maxSeverity != Severity.Error`). Les warnings n'empêchent pas le build.
- Affiche l'override éventuel du chemin d'installation (`EditorPrefs` clé `BAModBuilder.ModsLocalPath`).

### 2.2 `ModDiscovery` (`ModDiscovery.cs`)

- Racine des mods : constante `ModsRootAssetPath = "Assets/Mods"`.
- Découvre chaque asset `BAModManifest` (`AssetDatabase.FindAssets("t:BAModManifest")`) et construit un `DiscoveredMod` immuable :
  `Manifest`, `ManifestAssetPath`, `ModFolderAssetPath`, `ModFolderAbsolutePath`, `AsmdefAssetPath`, `AsmdefName`, `PlayerAssembly` (record `CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)`), `EditorCompiledDllPath` (`Library/ScriptAssemblies/<AsmdefName>.dll`), `DisplayNameOrModId`.
- Fallback pratique : si `Manifest.ModAssembly` n'est pas assigné mais qu'**exactement un** `.asmdef` existe dans le dossier du mod, il est auto-détecté (info, pas erreur).

### 2.3 `ModValidator` — TOUTES les règles (`ModValidator.cs`)

Sévérités : `Info < Warning < Error`. Chaque règle avec sa condition exacte :

| # | Règle | Sévérité | Condition de déclenchement |
|---|---|---|---|
| 1 | `RuleManifestLocation` | **Error** | le manifest n'est pas directement sous `Assets/Mods/<ModId>/` ; **ou** le nom du dossier ≠ `ModId` (comparaison ordinale sensible à la casse) ; **ou** `ModId` vide |
| 2 | `RuleUniqueModId` | **Error** | un autre mod a le même `ModId` (ordinal) |
| 3 | `RuleUniqueAsmdefName` | **Error** | un autre mod a le même nom d'asmdef (le nom de DLL livrée doit être unique) |
| 4 | `RuleAsmdefPresent` | **Error** / Info | Error si aucun asmdef (ni assigné ni trouvé) ; Info si non assigné mais auto-détecté ; Error si assigné mais chemin non résoluble |
| 5 | `RuleEditorCompiledDllExists` | **Error** | `Library/ScriptAssemblies/<AsmdefName>.dll` absent (l'asmdef ne compile pas) |
| 6 | `RuleCanonicalPrecompiledDrift` | **Error** (+ QuickFix « Sync Asmdef ») | asmdef illisible ; **ou** `overrideReferences != true` ; **ou** il manque au moins une des 32 DLL canoniques dans `precompiledReferences`. Le QuickFix force `overrideReferences: true` et ajoute toutes les DLL manquantes |
| 7 | `RuleRegisterModClass` | **Error** / Warning | Error si l'assembly compilée n'a aucun `[assembly: RegisterModClass(...)]` ; Error si l'attribut n'a pas de type ; Error si le type enregistré n'implémente pas `BAModAPI.IModBigAmbitions` ; **Warning** si la classe n'a aucun attribut dont le nom commence par `ModEntryOn`/`ModEntryMain` |
| 8 | `RuleBundleScoping` | **Error** | `EffectiveAssetBundleName` indéterminable (AssetBundleName ET ModId vides) ; **ou** un asset assigné au bundle du mod vit **hors** du dossier du mod |
| 9 | `RuleMacBuildSupport` | **Error** | `TargetPlatforms` inclut Mac mais le module « Mac Build Support (Mono) » n'est pas installé dans l'éditeur |
| 10 | `RuleRelativeAssetBundlePathsConvention` | Warning | un chemin retourné par `RelativeAssetBundlePaths` contient un segment `/Windows/`, `/Mac/` ou `/Linux/` — il faut le chemin « plat » (`AssetBundles/x.unity3d`), le loader runtime insère le segment plate-forme lui-même |
| 11 | `RuleEnumsTxtSyntax` | Warning | une ligne non vide/non-commentaire (`#`) de `Enums.txt` ne matche pas la regex `^\w+(\.\w+)+$` (forme `Namespace.EnumName[...]`) |
| 12 | `RuleDependenciesFolderShape` | **Error** | le dossier `Dependencies/` contient un sous-dossier (doit être plat) ; **ou** un fichier non-`.dll` (hors `.meta`) |
| 13 | `RuleLocalesFolderShape` | **Error** / Warning | Error si `LocalesFolder` référencé mais absent sur disque ; Warning s'il est vide |

Le dossier `Dependencies/` est résolu soit via `Manifest.DependenciesFolder`, soit **par convention** `Assets/Mods/<ModId>/Dependencies` (`GetDependenciesFolderAssetPath`).

### 2.4 `ModPackager` — build en 3 étapes (`ModPackager.cs`)

File d'attente mono-slot (un seul `AssemblyBuilder` à la fois dans Unity), pilotée par callbacks, états `BuildState { Idle, Queued, CompilingAssembly, BuildingBundles, Copying, Done, Failed }`, événement `ModPackager.JobChanged`.

**Étape 1 — Compilation « Player mode »** :
- `AssemblyBuilder(tempDllPath, player.sourceFiles)` vers `Temp/ModBuilder/<ModId>/<AsmdefName>.dll`.
- `buildTarget = StandaloneWindows64`, `buildTargetGroup = Standalone`, defines du player + `referencesOptions = UseEngineModules`, `CodeOptimization.Release`, `ApiCompatibilityLevel.NET_Unity_4_8` (profil .NET Framework 4.x, celui du jeu).
- Références = `compiledAssemblyReferences` + `assemblyReferences.outputPath` de l'assembly player.
- **La même DLL sert Windows et Mac** (Mono managé) — seuls les AssetBundles sont par plate-forme.

**Étape 2 — AssetBundles par plate-forme** :
- Si `Manifest.AssetBundleName` vide → étape sautée (mod code-only, ex. Example-Options).
- `EnsureModAssetsAssignedToBundle` : assigne **automatiquement** au bundle tout asset du dossier du mod, **sauf** : le manifest, l'asmdef, `Enums.txt`, le dossier `Locales/`, le dossier `Dependencies/`, et les fichiers `.cs`/`.asmdef`/`.dll`. (Donc pas besoin de tagger à la main ; attention : le thumbnail à la racine part aussi dans le bundle.)
- Nom du bundle splitté sur le dernier `.` en (nom, variant) — `backalleydealer.unity3d` → nom `backalleydealer`, variant `unity3d`.
- `BuildPipeline.BuildAssetBundles(dir, ChunkBasedCompression, target)` pour chaque cible de `TargetPlatforms` (Windows → `StandaloneWindows64`, Mac → `StandaloneOSX`), dossiers `Windows/` et `Mac/`.

**Étape 3 — Copie vers `Output/<ModId>/`** (le dossier est supprimé puis recréé à chaque build) :

```
Output/<ModId>/
  <AsmdefName>.dll
  AssetBundles/
    Windows/<bundle>.unity3d          (+ <bundle>.unity3d.manifest)
    Mac/<bundle>.unity3d              (+ .manifest)
  Dependencies/*.dll                  (copie plate des .dll uniquement)
  Locales/*.json                      (copie plate, .meta exclus)
  enums.txt                           (copie de Manifest.EnumsFile, nom forcé en minuscules)
```

Puis, si « Build + Install », `ModInstaller.Install(mod, outputDir)`.

### 2.5 `ModInstaller` (`ModInstaller.cs`)

- Chemin d'installation : **`%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\<ModId>\`** (dérivé de `LocalApplicationData` avec le swap `\Local` → `\LocalLow`).
- Override possible via `EditorPrefs` clé **`BAModBuilder.ModsLocalPath`**.
- L'installation **supprime** le dossier existant puis copie récursivement `Output/<ModId>/`.
- Refuse d'installer si le parent de `ModsLocal` n'existe pas (jeu non installé).
- `ModsLocal` est aussi le dossier ouvert par « Browse mod folder » du Mod Creator in-game ; l'upload Workshop se fait **depuis le jeu** (menu `Mods > Mod Creator`), avec un thumbnail à la racine du dossier du mod (max 1 Mo, limite Steam — cf. README).

### 2.6 `DependencyActions` — embarquer une DLL tierce (ex. HarmonyX)

Chemin exact pour une dépendance managée (`DependencyActions.cs`) :

1. Bouton **« Add Dep »** dans le Mod Builder (ou `AddDependency(mod, cheminAbsoluDll)`).
2. La DLL est copiée dans `Assets/Mods/<ModId>/Dependencies/` (dossier créé au besoin, **plat**, `.dll` uniquement — règle 12 du validateur).
3. Import settings appliqués via `PluginImporter` : `SetCompatibleWithAnyPlatform(false)`, Editor + StandaloneWindows/Win64/OSX/Linux64 activés, et surtout **`m_IsExplicitlyReferenced = true`** (propriété sérialisée flippée à la main car non publique en 2022 LTS) — la DLL n'est donc PAS auto-référencée par les autres assemblies du projet.
4. `WireAsmdefForDependency` : l'asmdef du mod passe à `overrideReferences: true`, reçoit les 32 DLL canoniques **et** le nom de fichier de la dépendance dans `precompiledReferences`.
5. Au packaging, `CopyDependencies` copie tous les `.dll` de `Dependencies/` vers `Output/<ModId>/Dependencies/` ; le runtime du jeu charge ce dossier (c'est le seul endroit prévu pour des DLL tierces).
6. `FixDependencyImportSettings(mod)` répare les settings d'une DLL déposée à la main ; `SyncAllModAsmdefs()` resynchronise tous les asmdefs après une mise à jour de la liste canonique.

**Pour HarmonyX** : déposer `0Harmony.dll` via « Add Dep » suffit — elle sera compilée contre par l'éditeur, listée dans `precompiledReferences`, expédiée dans `Dependencies/` et chargée par le jeu à côté de la DLL du mod.

### 2.7 `AsmdefFile` (`AsmdefFile.cs`)

Wrapper JSON round-trip (Newtonsoft `JObject`, champs inconnus préservés). Propriétés : `Name` (ro), `OverrideReferences`, `AutoReferenced`, `PrecompiledReferences` (List\<string\>), `Load(path)` / `Save()`.

### 2.8 Anatomie d'un asmdef de mod (référence : `BackAlleyDealer.asmdef`)

```json
{
  "name": "BackAlleyDealer",
  "rootNamespace": "BackAlleyDealer",
  "references": [],
  "overrideReferences": true,
  "precompiledReferences": [ /* les 32 DLL canoniques, voir §4 */ ],
  "autoReferenced": false,
  "defineConstraints": ["BA_GAME_DLLS_IMPORTED"],
  "allowUnsafeCode": false,
  "noEngineReferences": false
}
```

- `defineConstraints: ["BA_GAME_DLLS_IMPORTED"]` : l'assembly du mod ne compile **que** si les DLL du jeu ont été importées (sinon le clone frais casserait).
- `autoReferenced: false` : le mod ne pollue pas les autres assemblies.
- Curiosité/anomalie dans les exemples : `Example-Furniture` et `Example-Vehicle` référencent `GUID:43415f9a...` (= `BackAlleyDealer.asmdef`), tandis que `Example-Options` et `Example-BusinessType` référencent `GUID:776d03a35f1b52c4a9aed9f56d7b4229` qui **n'existe nulle part dans le repo** (référence morte, ignorée par Unity). Un nouveau mod peut laisser `references: []`.

---

## 3. `BAModManifest` (ScriptableObject, `Assets/Editor/ModBuilder/BAModManifest.cs`)

Créé via menu **`Big Ambitions/Mod Manifest`** (`CreateAssetMenu`), fichier `ModManifest.asset` à la racine du dossier du mod. Champs complets :

| Champ | Type | Rôle |
|---|---|---|
| `ModId` | `string` | identifiant = **nom exact du dossier** sous `Assets/Mods/` (règle 1) ; c'est aussi le nom du dossier installé dans `ModsLocal` |
| `DisplayName` | `string` | nom lisible dans le menu mods |
| `Author` | `string` | — |
| `Version` | `string` (défaut `"0.1.0"`) | — |
| `AssetBundleName` | `string` | identifiant Unity **complet avec variant** (ex. `example-furniture.unity3d`) ; vide = pas de bundle |
| `ModAssembly` | `AssemblyDefinitionAsset` | l'asmdef du mod → produit la DLL livrée |
| `LocalesFolder` | `DefaultAsset` (dossier) | JSON de locales, copiés tels quels vers `Output/<ModId>/Locales/` |
| `DependenciesFolder` | `DefaultAsset` (dossier) | DLL tierces → `Output/<ModId>/Dependencies/` (fallback conventionnel `<mod>/Dependencies`) |
| `EnumsFile` | `TextAsset` | `Enums.txt` → copié en `Output/<ModId>/enums.txt` |
| `TargetPlatforms` | `ModTargetPlatforms` (flags) | plates-formes des AssetBundles |
| `EffectiveAssetBundleName` | propriété calculée | `AssetBundleName` ou, si vide, `ModId.ToLowerInvariant()` |

### 3.1 `TargetPlatforms = 3` ?

```csharp
[Flags] public enum ModTargetPlatforms { None = 0, Windows = 1 << 0, Mac = 1 << 1 }
```

**3 = Windows (1) | Mac (2)** — la valeur par défaut et celle de tous les manifests d'exemple. Résolution en cibles de build : Windows → `StandaloneWindows64`, Mac → `StandaloneOSX` (`ModPackager.ResolveTargets`). Linux n'est pas proposé (le mapping `PlatformFolderName` connaît `Linux` mais aucun flag ne l'active).

### 3.2 Éditeur custom (`BAModManifestEditor.cs`)

Inspecteur qui affiche la validation en direct (cache invalidé sur `projectChanged`/`compilationFinished`), boutons « Re-validate », QuickFix inline, « Open Mod Builder Window ».

### 3.3 Exemple de manifest sérialisé (BackAlleyDealer)

```yaml
ModId: BackAlleyDealer
DisplayName: Back Alley Dealer
Author: Big Ambitions Mod Template
Version: 0.1.0
AssetBundleName: backalleydealer.unity3d
ModAssembly: {guid: 43415f9a12052d84fa4a180264d76a9d}
LocalesFolder: {guid: 33a84bb8d4bf10948b4272ee8b0c2051}
DependenciesFolder: {fileID: 0}
EnumsFile: {guid: e86dd53be89cc0b4cb33b2a8fca6a567}
TargetPlatforms: 3
```

### 3.4 `EnumsFile` — format et rôle de `Enums.txt`

Contenu intégral du `Enums.txt` de BackAlleyDealer :

```
Dialogs.CallDialogType.backalleydealer_calldialogtype
```

- Format : une entrée par ligne, `Namespace.EnumType.nouvelle_valeur` (validateur : regex `^\w+(\.\w+)+$`, lignes vides et commentaires `#` autorisés — Warning seulement en cas de non-conformité).
- Rôle : déclarer au runtime **de nouvelles valeurs injectées dans des enums du jeu**. Côté code, la valeur est obtenue par `(CallDialogType)ModEnumHash.GetSafeHash("backalleydealer_calldialogtype")` — hash stable dérivé du nom, donc sérialisable dans la sauvegarde et sans collision entre mods. Le fichier `enums.txt` livré permet vraisemblablement au jeu de pré-enregistrer/mapper ces valeurs (nom → hash) au chargement du mod, avant l'exécution du code.
- BackAlleyDealer s'en sert pour créer un `CallDialogType` custom et y attacher son dialogue via `CallDialogFactory.RegisterDialog(dialogType, () => new BackAlleyDealerDialog())`.

---

## 4. Bootstrap : import des DLL du jeu

### 4.1 `SteamInstallLocator` (`Assets/Editor/Bootstrap/SteamInstallLocator.cs`)

- AppId Steam : **1331550** ; nom fallback `"Big Ambitions"` ; dossier managé : `Big Ambitions_Data/Managed`.
- Localisation : registre Windows (`HKCU\Software\Valve\Steam\SteamPath`, puis `HKLM\SOFTWARE\[WOW6432Node\]Valve\Steam\InstallPath`), fallback `Program Files (x86)\Steam` ; puis parse de `steamapps/libraryfolders.vdf` et des `appmanifest_*.acf` (regex simples sur le format VDF) pour trouver `installdir` et **`buildid`**.
- API : `TrySteamAutoDetect(out SteamInstallInfo)`, `IsValidBigAmbitionsInstall(path)`, `GetManagedFolder(path)`, `TryReadBuildIdFor(path, out buildId)`. `SteamInstallInfo { InstallPath, BuildId, IsValid }`.
- Windows-only (registre) — sur macOS/Linux il faut pointer le chemin à la main dans la Welcome window.

### 4.2 `CanonicalGameDlls` — les 32 DLL référencées par chaque mod

Liste `CanonicalGameDlls.All` (fichiers copiés dans `Assets/_BaDependencies/GameDlls/`, gitignorés) :

```
BehaviorDesigner.Runtime.dll, BigAmbitions.AI.dll, BigAmbitions.Characters.dll,
BigAmbitions.DebugMode.dll, BigAmbitions.dll, BigAmbitions.Factories.dll,
BigAmbitions.GameAnalytics.dll, BigAmbitions.InputSystem.dll,
BigAmbitions.InteriorDesigner.dll, BigAmbitions.Items.dll, BigAmbitions.Legacy.dll,
BigAmbitions.ModAPI.dll, BigAmbitions.ModsInternal.dll, BigAmbitions.Neighborhoods.dll,
BigAmbitions.PlacementSystem.dll, BigAmbitions.Seasons.dll, BigAmbitions.SoundSystem.dll,
DayNightCycle.dll, DOTween.dll, DOTween.Modules.dll, ExternalPlugins.dll,
Facepunch.Steamworks.Win64.dll, Google.OrTools.dll, Google.Protobuf.dll,
HBAO.HighDefinition.Runtime.dll, HGExtensions.dll, HGPlugins.dll,
JimmysUnityUtilities.dll, NaughtyAttributes.Core.dll, OdinSerializer.dll,
System.Runtime.CompilerServices.Unsafe.dll, UnityUIExtensions.dll
```

Raison d'être documentée dans le fichier : les DLL du jeu sont importées avec `isExplicitlyReferenced: 1` pour éviter que les packages Unity du projet (visualscripting, addressables…) ne les auto-référencent et créent des collisions de types (ex. `BigAmbitions.Legacy.PlayerPref` vs `PlayerPrefs`). D'où l'obligation `overrideReferences: true` + liste complète dans chaque asmdef de mod.

**Note pour le coop** : `Facepunch.Steamworks.Win64.dll` fait partie de la liste canonique — chaque mod y a donc accès de droit.

### 4.3 `GameDllImporter` (`GameDllImporter.cs`)

- Copie les 32 DLL depuis `<install>/Big Ambitions_Data/Managed/` vers `Assets/_BaDependencies/GameDlls/` (`CopyIfChanged` : skip si taille + mtime identiques).
- Écrit un tracker `UserSettings/BAModBuilder.ImportedDlls.json` `{installPath, buildId, importedAtUtc, dllCount}` — le `buildid` Steam permet d'afficher « game updated — re-import » après un patch.
- États (`GameDllState`) : `BigAmbitionsNotFound` → `ReadyToImport` → `UpToDate` / `UpdateAvailable` (buildid divergent).
- **GUID déterministes** : chaque `.dll.meta` reçoit un GUID = MD5 de `"BAModTemplate.GameDllGuid:" + nomdll.ToLowerInvariant()` (`ComputeDeterministicDllGuid`) — ainsi les références sérialisées survivent aux réimports sur toutes les machines.
- Import settings des DLL du jeu : AnyPlatform off, Editor + Standalone Win/Win64/OSX/Linux64 on, `m_IsExplicitlyReferenced=1`, `m_ValidateReferences=0` (références croisées non résolues tolérées).
- **Define `BA_GAME_DLLS_IMPORTED`** (constante `ImportedDefine`) : ajouté aux Scripting Define Symbols du groupe Standalone après import (`ProjectSettings.asset` ligne 822 : `Standalone: BA_GAME_DLLS_IMPORTED`). Un guard `[InitializeOnLoad] ImportedDefineGuard` réconcilie le define à chaque ouverture d'éditeur (`ReconcileImportedDefine`) : il le retire si le tracker ou les DLL manquent — protège les clones frais contre un define commité par accident. Tous les asmdefs de mods (et `ItemControllerTools.Editor`) portent `defineConstraints: ["BA_GAME_DLLS_IMPORTED"]`.
- Clés `EditorPrefs` : chemin d'install `BAModBuilder.BigAmbitionsInstallPath`.

### 4.4 `WelcomeWindow` (`Assets/Editor/Branding/WelcomeWindow.cs`)

- Menu `Big Ambitions/Welcome`, auto-show à l'ouverture du projet (une fois par session, opt-out `EditorPrefs`, version de contenu `WelcomeVersion = 2`).
- Auto-détecte Steam au premier lancement, champ chemin + Browse + Auto-detect, indicateur de statut coloré (rouge/ambre/vert selon `GameDllState`), bouton principal « Import DLLs from Steam » / « Update DLLs… » / « Re-import DLLs » → `GameDllImporter.Import(path)`.
- Quick start affiché : *1. dossier sous `Assets/Mods/<YourModId>/` ; 2. ModManifest + asmdef ; 3. scripts/assets ; 4. Mod Builder ; 5. Build + Install.*

### 4.5 `ItemControllerAutoSetupEditor` (bonus outillage)

Custom editor sur `BigAmbitions.Items.ItemController` : bouton « Auto Configure ItemController References » qui rebâtit les champs sérialisés `renderers`, `navMeshTargets` (tag `NavMeshTarget`), `attachmentPoints` (`AttachmentPoint`), `colliders` (hors tags `GroundIndicator`/`AttachmentPointIndicator`), `navMeshObstacles`, `screenVideoController` (`ScreenVideoController`), `groundIndicators` (struct `GroundIndicator { transform, renderer }`) — en **préservant** `itemName` (contrairement au bouton du jeu). Types du jeu impliqués : `ItemController`, `AttachmentPoint`, `ScreenVideoController`, `GroundIndicator` (namespace `BigAmbitions.Items`).

---

## 5. API du jeu utilisée par les mods d'exemple

C'est notre unique fenêtre sur l'API sans les DLL. Convention : *« ns : »* = namespace certain (présent dans les `using` et sans ambiguïté) ; *« ns ? »* = namespace inféré parmi les imports du fichier (non vérifiable sans les DLL).

### 5.1 Enregistrement de contenu (`BAModAPI` / `BAModAPI.Services`)

```csharp
// BAModAPI.Services
AssetBundle AssetService.GetBundle(string modId, string relativeBundlePath);

// BAModAPI ou BAModAPI.Services (fichiers: ExampleBusinessTypeMod.cs, ExampleVehicleMod.cs)
ModdingAPI.RegisterModBusinessType(BusinessType bt);
ModdingAPI.UnregisterModBusinessType(BusinessType bt);
ModdingAPI.RegisterModVehicleType(VehicleType vt);
ModdingAPI.UnregisterModVehicleType(string vehicleTypeName);

// BAModAPI
int ModEnumHash.GetSafeHash(string name);
```

### 5.2 Items — ns : `BigAmbitions.Items`

```csharp
ItemsGetter.AllItems                    // IEnumerable<Item> (tous les items, jeu + mods)
ItemsGetter.IsModItem(string itemName)  // bool
ItemsGetter.RegisterModItem(Item item)
ItemsGetter.UnregisterModItem(string itemName)

class Item : ScriptableObject {
    string itemName;                    // clé localisée, ex. "example-furniture:itemname_gigacounter"
    ItemType type;                      // flags ; testé: (item.type & ItemType.ShowcaseShelf) != 0
    string[] itemsThatCanShowcase;      // patché à chaud par Example-BusinessType
    // champs sérialisés vus dans GigaCounter.asset : wholesalePrice, defaultMarketPrice,
    // productSalesRatio, boxSize, showBoxAsHandItem, isFurniture, gridSize,
    // customColorChannels, customizationColors[], tags[], database, leanBackSitAnimation,
    // defaultWorldSpaceKey, itemPanelMetaView, ...
}

// ShelfController — ns ? (BigAmbitions.Items ou Buildings) — fichier ExampleBusinessTypeMod.cs
ShelfController.RegisterItemToShow(string itemToShow, string shelfItemName, string basedOnItemName);
ShelfController.UnregisterItemToShow(string itemToShow);

// Outillage éditeur : ItemController, AttachmentPoint, ScreenVideoController, GroundIndicator
```

### 5.3 Business types — fichier `ExampleBusinessTypeMod.cs` (ns : `Buildings` pour BusinessType — via `using Buildings;`)

```csharp
class BusinessType : ScriptableObject { /* champs vus dans ToyStore.asset :
    businessTypeName, suitableBuildingType, tags[] ("ba:businesstag_*"), simulator,
    spawnCustomers, businessProducts[{itemName, impact}], productSources[],
    employeePrimarySkills[], customerType, businessRequirements[], customerDemandSets[],
    logoShapes[], dayFactorMultipliers[], icon, cityMapFilterColor, callDialogType ... */ }

// Import/export (importateur du port) :
var settings = (ImportExportSettings)BuildingHelper
    .GetBuilding(new Address("ba:street_pier", 4)).SpecialService.settings;   // Buildings/Helpers
settings.itemsAvailable.Add("example-businesstype:itemname_falcontoy");       // List<string>
settings.itemsAvailable.Remove(...);
```

### 5.4 Véhicules — ns : `Vehicles.VehicleTypes` (+ `Helpers`)

```csharp
VehicleTypeHelper.GetVehicleTypeNames()          // IEnumerable<string>
VehicleTypeHelper.IsModVehicleType(string name)  // bool
VehicleTypeHelper.GetVehicleType(string name)    // VehicleType

class VehicleType : ScriptableObject {
    string vehicleTypeName; bool taxDeductible; float maxFuel;
    // vu dans TurboHonza.asset : price, isATruck, isHandVehicle, maxCargoCapacity,
    // maxSpeed, enginePower, brakeForce, turnRadius, damageIntensity, fitsHandTruck,
    // fitsFlatbed, autoParkSupported, hasRadio, enclosed, canGetDirty, dirtinessTimer, ...
}

class VehicleInstance {                      // ns ? (fichier BackAlleyDealerVehicleService.cs)
    VehicleInstance(string vehicleName);
    string id; string vehicleColorName; float fuel;
}

class VehicleContractSettings {              // écran d'achat de véhicule (composant UI)
    static bool disableDeliveryOnNextInit;
    VehicleForSale selectedVehicleForSale;   // .VehicleName, .GetPurchasePrice(), .GetInitialColor()
    ShowcaseVehicle selectedVehicle;         // .Purchase() : bool  (noms de types exacts inconnus)
}

// Helpers (ns : Helpers)
VehicleHelper.CreateAndSpawnVehicle(VehicleInstance vi, Vector3 pos, Quaternion rot);
VehicleHelper.AllPlayerVehicles;             // IEnumerable<VehicleController> (.vehicleInstance.id)
VehicleHelper.TeleportVehicleToGround(VehicleController vc, Vector3 pos, Quaternion rot);
VehicleParkingHelper.TryGetRandomParkingGarageSpot(string sceneRootPath, out Vector3, out Quaternion);
// chemin de scène utilisé : "BuildingBlocks/BuildingBlock(5,1)/Parking01Exterior"
UuidHelper.GenerateBase64Uuid();             // string
```

### 5.5 Contacts, ville, messages — fichier `BackAlleyDealerCity.cs` (ns : `Dialogs`, `Entities`, `UI.Smartphone.Apps.Contacts`)

```csharp
// Adresse (ns ? — utilisée avec using Entities/Streets côté Dialog et sans import dédié côté Init :
// probablement namespace global ou BAModAPI)
new Address("backalleydealer:street_anonAve", 420);   // (string streetKey, int number)
address.ToFormattedString();

// Contact (Entities ? / UI.Smartphone.Apps.Contacts ?)
Contact Contact.GetContact(string contactId, ContactCategoryName category, string descriptionKey);
contact.callDialogTypeOverride = (CallDialogType)hash;
contact.messagesQueue;                                 // ICollection<...>
contact.Address;                                       // Address (peut être null)
contact.SendMessage(TextMessage msg, bool sendNotificationInstantly = false);
contact.ReceivePlayerMessage(TextMessage msg);

enum ContactCategoryName { FurnitureAndEquipment, ... } // ns : UI.Smartphone.Apps.Contacts

// TextMessage (Entities ?)
new TextMessage(string key);
new TextMessage(string key, Dictionary<string,string> data, bool flag, bool read);
new TextMessage(string key, Dictionary<string,string> data, bool flag);
new TextMessage(string key, read: true);

// Icônes de contact
GlobalReferences.Instance.contactIcons;   // Sprite[] — le mod l'agrandit et y ajoute son sprite

// Services (ns : Services) — magasin "contrat" du dealer
ContractItemsForSaleService.SetItemsForContact(string contactId, HashSet<string> itemNames);
ContractItemsForSaleService.SetVehiclesForContact(string contactId, HashSet<string> vehicleNames);
ContractItemsForSaleService.RemoveContact(string contactId);
ContractItemsForSaleService.SetContactForAddress(Address address, string contactId);
ContractItemsForSaleService.RemoveContactForAddress(Address address);
```

### 5.6 Système de dialogue — fichier `BackAlleyDealerDialog.cs` (ns : `Dialogs`, `UI.Dialog`, `UI.Notification`)

```csharp
class Dialog {                          // classe de base à hériter
    string npcNameKey;                  // clé de locale du PNJ
}

DialogController.current;               // singleton
DialogController.current.ShowEntry(DialogEntry entry);
DialogController.current.contact;       // Contact de la conversation courante
DialogController.current.dialogType;    // DialogType.PhoneCall | DialogType.Physical
DialogController.current.FinishDialog;  // Action
DialogController.current.CancelDialog;  // Action
DialogController.current.GetInputComponent<T>();          // ex. FurnitureDeliveryContractSettings
DialogController.current.GetInputTransform<T>(object arg); // ex. VehicleContractSettings

class DialogEntry {
    object messageData;                       // texte affiché ("clé".Localize())
    TemplateType Template;                    // enum imbriqué : Text, Input
    InputTemplateName InputTemplate;          // enum imbriqué : None, FurnitureDeliverySettings,
                                              //   FurnitureDeliveriesList, VehicleContractSettings
    string headerKey;
    string ConfirmTextOverride; string SecondOptionTextOverride;
    Func<DialogEntry> OnConfirm, OnSecondOption;   // retourner null = rester sur place
    Action OnCancel, OnVisible;
    TextMessage onCancelMessage;
    void ShowEntry();                          // raccourci self-show
}

enum CallDialogType { ... }                    // ns : Dialogs (extensible via Enums.txt)
CallDialogFactory.RegisterDialog(CallDialogType type, Func<Dialog> factory);

// UI.Notification
Notifications.ShowError(string titleKey, string bodyKey);
Notifications.ShowError(string key);
```

### 5.7 Sauvegarde & économie — ns : `BigAmbitions.SaveSystem.Legacy`, `Buildings`, `Helpers`

```csharp
SaveGameManager.Current;                             // sauvegarde active
SaveGameManager.Current.BuildingRegistrations;       // items avec .RentedByPlayer, .BusinessName
SaveGameManager.Current.FurnitureDeliveryContracts;  // List<FurnitureDeliveryContract> (.Add/.Remove)
SaveGameManager.Current.VehicleInstances;            // List<VehicleInstance>

class FurnitureDeliveryContract {                    // Buildings ?
    Address fromAddress, toAddress;
    int dayOfDelivery, hourOfDelivery;
    List<FurnitureDeliveryItem> itemsToDeliver;
    ... deliveryFee;
}
class FurnitureDeliveryItem { string itemName; int amount; ... pricePerUnit; }
class FurnitureDeliveryContractSettings {            // composant input du dialogue
    Address selectedAddress; (int,int) selectedDeliverySlot;
    int TotalItemsToDeliverAmount; IEnumerable<...> itemsToDeliver; // {itemName, amount, price}
    static ... deliveryFee;
}

BuildingHelper.GetBuildingRegistration(Address a);   // ns : Helpers → .BusinessName
BuildingHelper.GetBuilding(Address a);               // → .SpecialService.settings
BuildingManager.Instance.cityBuildingController.customPositions; // List<Transform-like> (.position, .rotation)

// Argent (ns ? global/Helpers — fichier BackAlleyDealerVehicleService.cs)
GameManager.ChangeMoneySafe(decimal montantNégatif, TransactionInfo info, bool showNotification); // bool
GameManager.Instance.playerController;               // .transform
new TransactionInfo(LegacyRef.Transaction.VehicleBought,
                    Dictionary<string,string> data, bool taxDeductible);

// Constantes legacy (ns : BigAmbitions.SaveSystem.Legacy — classe LegacyRef)
LegacyRef.Transaction.VehicleBought
LegacyRef.MessageType.ContactsMessagePlayerCancelCall
LegacyRef.MessageType.DialogFurnitureStoreOnContractSettingsSetPlayer
LegacyRef.MessageType.DialogFurnitureStoreOnContractSettingsSetPlayerBusinessName
LegacyRef.MessageType.DialogVehicleStoreVehiclePurchasedPlayer
LegacyRef.MessageType.DialogVehicleStoreVehiclePurchasedManager
```

### 5.8 Options de mod — fichier `ExampleOptionsLogic.cs` (ns : `BAModAPI` + `BigAmbitions.Mods`)

`ModOptions` est une API fluente (chaque `Add*` retourne `this`). **Toutes** les méthodes vues :

```csharp
var options = new ModOptions()
    .AddHeader(string headerLocaleKey)
    .AddToggle(string saveKey, string labelKey, bool defaultValue, Action<bool> onChanged)
    .AddSlider(string saveKey, string labelKey, int min, int max, int value,
               Action<int> onChanged, string valueFormatKey /* optionnel, ex. "{value}" */)
    .AddSlider(string saveKey, string labelKey, int min, int max, int value,
               Action<int> onChanged)                       // surcharge sans suffixe
    .AddDropdown(string saveKey, string labelKey, string[] choiceLocaleKeys,
                 int selectedIndex, Action<int> onChanged)
    .AddSplitter();

OptionsService.Register(string modId, ModOptions options);
OptionsService.RemoveModOptions(string modId);
```

Les `saveKey` (`"example_toggle"`, …) suggèrent une persistance automatique des valeurs par le jeu ; le format du slider (`"sliderinmydms_slider_value": "{value}"`) est une clé de locale avec placeholder `{value}`.

### 5.9 Localisation — ns : `Localizor` (+ extensions)

```csharp
"backalleydealer:dialog_start".Localize();       // string → string localisée
itemName.GetLocalization();                       // idem (variante)
hour.GetFormattedTime();                          // int → "8:00" etc. (ns ? Extensions/Helpers)
```

---

## 6. Locales

- **Emplacement** : dossier référencé par `Manifest.LocalesFolder` (par convention `Assets/Mods/<ModId>/Locales/`), copié **tel quel** (plat, `.meta` exclus) vers `Output/<ModId>/Locales/`.
- **Fichiers** : un JSON par langue, nommé par code de langue : `en.json`, `nl.json` observés. Les langues supportées = celles du jeu ; le SDK ne fournit pas de liste (en + nl seulement dans les exemples ; `fr.json` suivra le même schéma). Objet JSON plat `"clé": "valeur"`. Remarque : le `en.json` de BackAlleyDealer contient une **virgule terminale** — le parseur du jeu est donc tolérant (mais ne pas s'y fier).
- **Convention de clés** : préfixe `<modid-minuscule>:` — `backalleydealer:dialog_start`, `example-furniture:itemname_gigacounter`, `example-businesstype:businesstype_toystore`, `example-vehicle:vehicletype_turbohonza`. Les clés du jeu de base utilisent `ba:` (`ba:itemname_roundedshelf`, `ba:street_pier`, `ba:skill_customerservice`…). Le préfixe n'est pas techniquement imposé (Example-Options utilise `sliderinmydms_...` sans deux-points, et le contact du dealer est la clé `backalleydealer-dealername` avec tiret), mais préfixer évite les collisions globales.
- **Sous-conventions** vues : `itemname_*`, `vehicletype_*`, `businesstype_*` (+ `_alias`), `street_*`, `dialog_*`, `textmessage_*`, `source_furniture_<item>` (fournisseur affiché pour un meuble : `"source_furniture_gigacounter": "Example Furniture"`).
- **Accès côté code** : tout passe par les clés — `"clé".Localize()` / `.GetLocalization()` (namespace `Localizor`), et la quasi-totalité des API du jeu prennent directement des clés (`npcNameKey`, `TextMessage(key, …)`, `Notifications.ShowError(key)`, `ModOptions.Add*(…, labelKey, …)`, `Item.itemName`, `Address(streetKey, n)`…). Le jeu résout dans la langue de l'utilisateur avec fallback (vraisemblablement `en`).

---

## 7. Asset bundles

- **Déclaration** : `Manifest.AssetBundleName` = identifiant complet **avec variant** (`backalleydealer.unity3d`). Convention : nom = ModId en minuscules + variant `.unity3d`. Chaque mod = un seul bundle.
- **Assignation** : automatique au build (`ModPackager.EnsureModAssetsAssignedToBundle`) — tout asset du dossier du mod hors code/manifest/locales/deps/enums. La règle 8 interdit qu'un asset hors du dossier du mod soit dans le bundle.
- **Côté mod (runtime)** :
  ```csharp
  private const string BundleKey = "AssetBundles/example-furniture.unity3d";
  public string[] RelativeAssetBundlePaths => new[] { BundleKey };   // déclaré à l'interface

  public Task OnLoadAsync(ModContext context)
  {
      var bundle = AssetService.GetBundle(context.ModId, BundleKey); // même clé
      var item = bundle.LoadAsset<Item>("Assets/Mods/Example-Furniture/GigaCounter.asset");
      ...
  }
  ```
- **Chemin plat obligatoire** : `AssetBundles/<nom>.unity3d` **sans** segment `Windows/`/`Mac/` — le loader du jeu insère le segment plate-forme selon l'OS (règle 10, Warning sinon). Sur disque le paquet contient bien `AssetBundles/Windows/...` et `AssetBundles/Mac/...`.
- **Chargement d'assets** : toujours par **chemin d'asset complet du projet SDK** (`Assets/Mods/<ModId>/Fichier.ext`), y compris pour les sprites (`bundle.LoadAsset<Sprite>("Assets/Mods/BackAlleyDealer/backalleydealer-dealername.png")`).
- Un mod sans assets (Example-Options) : `AssetBundleName` vide + `RelativeAssetBundlePaths => Array.Empty<string>()`. Un mod peut aussi avoir un point d'entrée avec bundle et un autre sans (BackAlleyDealer : `Init` → vide, `City` → le bundle).
- Compression : `ChunkBasedCompression` (LZ4), un `.manifest` texte accompagne chaque bundle.

---

## 8. Implications pour le mod coop CoopAmbitions

### Ce que le SDK confirme / impose

1. **Structure à respecter au caractère près** : `Assets/Mods/CoopAmbitions/` avec `ModManifest.asset` (`ModId: CoopAmbitions` **identique au nom du dossier**, ordinal-sensible), `CoopAmbitions.asmdef` unique dans le dossier. Notre asmdef doit avoir `overrideReferences: true`, les **32 DLL canoniques** dans `precompiledReferences`, `autoReferenced: false` et `defineConstraints: ["BA_GAME_DLLS_IMPORTED"]` — sinon rejet en Error (règle 6) ou non-compilation. Le plus simple : copier l'asmdef de BackAlleyDealer et renommer.
2. **Point d'entrée** : `[assembly: RegisterModClass(typeof(CoopMod))]` + `[ModEntryOnCityLoad]` sur la classe est le bon moment pour le coop (le monde existe, on peut créer le `CoopRunner` `DontDestroyOnLoad`, localiser le joueur, ouvrir le lobby). On peut ajouter une seconde classe `[ModEntryOnMainMenuLoad]` (host/join depuis le menu) et/ou `[ModEntryOnInitializationLoad]` — le SDK démontre explicitement le multi-entrées par assembly. `RelativeAssetBundlePaths => Array.Empty<string>()` tant qu'on n'a pas d'avatar custom.
3. **`OnUnloadAsync` doit être réel** : le jeu décharge les mods à chaud. Il faut fermer sockets/lobby Steam, détruire `CoopRunner` et les avatars distants, dé-enregistrer tout hook — sous peine de fuites à la désactivation du mod dans le menu.
4. **Steamworks est officiel** : `Facepunch.Steamworks.Win64.dll` est dans la liste canonique — notre transport Steam relay est compilable sans aucune dépendance supplémentaire. Attention : c'est la variante **Win64** ; sur Mac le jeu embarque probablement une autre variante — à vérifier, mais `TargetPlatforms: 3` ne concerne que les bundles, et notre DLL unique est compilée en référencant la Win64. Prudence donc si on vise Mac (possibilité : `TargetPlatforms: Windows` = valeur `1` au début).
5. **Harmony passe par `Dependencies/`** : si on doit patcher (pause du temps côté client, interception d'achats…), déposer `0Harmony.dll` via « Add Dep » — dossier plat, `.dll` uniquement, câblage asmdef automatique, livraison dans `Output/CoopAmbitions/Dependencies/`. C'est le canal officiel : aucun risque de rejet du validateur.
6. **Pas de paquet à fabriquer à la main** : `Build + Install` produit `Output/CoopAmbitions/{CoopAmbitions.dll, Locales/, Dependencies/}` et l'installe dans `%LocalLow%\Hovgaard Games\Big Ambitions\ModsLocal\CoopAmbitions\`. L'upload Workshop se fait in-game (thumbnail < 1 Mo à la racine du dossier du mod).
7. **Locales** : nos `Locales/en.json` et `fr.json` sont conformes ; préfixer toutes les clés `coopambitions:` pour éviter les collisions, et utiliser `"clé".Localize()` (ns `Localizor`) pour tout texte affiché.

### Points d'API directement réutilisables pour le coop

- **UI sans assets** : `ModOptions`/`OptionsService` donne un panneau d'options natif (toggle « héberger au chargement », slider tick-rate, etc.) ; `Notifications.ShowError/…` pour les toasts de connexion ; le système `Dialog`/`DialogEntry`/`Contact`/`TextMessage` permet même une UX « téléphone » (un contact « Coop » qui invite/joint) sans une ligne d'UI custom.
- **État du monde** : `SaveGameManager.Current` (BuildingRegistrations, VehicleInstances, FurnitureDeliveryContracts) est l'instantané à répliquer ; `GameManager.ChangeMoneySafe(montant, TransactionInfo, bool)` est le point d'entrée « argent » côté réception d'événements distants ; `VehicleHelper.CreateAndSpawnVehicle` / `TeleportVehicleToGround` et `UuidHelper.GenerateBase64Uuid()` servent tels quels au spawn/positionnement des véhicules distants ; `GameManager.Instance.playerController.transform` est la source de la position locale (confirme `LocalPlayerLocator`).
- **Identifiants réseau stables** : `ModEnumHash.GetSafeHash(string)` fournit des hash stables inter-machines — utile pour encoder des types/enums dans le protocole. Si on étend un enum du jeu, ne pas oublier le `Enums.txt` (`Namespace.Enum.valeur`) référencé par le manifest.
- **Logging** : `context.Logger.Info(...)` — logger officiel par mod, à utiliser plutôt que `Debug.Log` pour le diagnostic réseau.

### Pièges identifiés

- Ne **jamais** committer `BA_GAME_DLLS_IMPORTED` dans un ProjectSettings partagé hors SDK : l'`ImportedDefineGuard` du SDK le gère, mais notre repo autonome doit documenter l'import via la Welcome window.
- Le validateur **instancie** nos classes de mod dans l'éditeur (`Activator.CreateInstance`) pour lire `RelativeAssetBundlePaths` : le constructeur et l'accès à cette propriété doivent être **sans effet de bord** (pas d'init Steam dans un constructeur !). Tout démarrage réseau doit vivre dans `OnLoadAsync`.
- `Address`, `GameManager`, `TransactionInfo`, `VehicleInstance` & co ont des namespaces incertains (probablement globaux/legacy) — à confirmer dès qu'on compile contre les vraies DLL ; ce document liste les `using` exacts de chaque fichier d'exemple pour s'y référer.
- Chaque mise à jour Steam du jeu change le `buildid` → réimporter les DLL (état `UpdateAvailable`) et re-tester : l'API `ModsInternal`/`ModAPI` peut bouger entre builds.
