# Guide Harmony expert — CoopAmbitions (Big Ambitions, Unity 2022.3 Mono)

*Rapport de recherche « production », 2026-08-31. Basé sur la lecture du code source réel de :
[pardeike/Harmony](https://github.com/pardeike/Harmony) (cloné dans `/home/user/harmony`, version 2.4.2, MIT),
[Nitrox](https://github.com/SubnauticaNitrox/Nitrox) (`/home/user/nitrox`, ~370 patches, le mod coop de référence),
[Combat Extended](https://github.com/CombatExtended-Continued/CombatExtended) (`/home/user/combatextended`, 105 fichiers de patches RimWorld),
[SMAPI](https://github.com/Pathoschild/SMAPI) (`/home/user/smapi`),
[Jotunn](https://github.com/Valheim-Modding/Jotunn) (`/home/user/jotunn`),
plus la doc officielle Harmony (dossier `Documentation/articles` du repo) et le wiki HarmonyX.
Les snippets marqués d'une source sont recopiés (parfois abrégés `// ...`) du code cité.*

**TL;DR pour les pressés** : on embarque **Lib.Harmony 2.4.2 « fat »** (un seul fichier
`0Harmony.dll`, build net472) dans `Dependencies/` du mod ; un patch = un fichier = une
classe `Type_Méthode_Patch` avec `TARGET_METHOD` résolu par expression lambda
(pattern Nitrox) ; application manuelle patch par patch sous try/catch avec rapport de
« smoke test » au chargement ; `harmony.UnpatchAll(harmony.Id)` à l'unload ;
`PacketSuppressor<T>` (compteur statique + `IDisposable`) dès le premier patch.
Le squelette complet est en §8.

---

## Table des matières

1. [Harmony vs HarmonyX vs MonoMod — que faut-il embarquer ?](#1)
2. [Anatomie des patches — le référentiel complet](#2)
3. [Organisation à grande échelle — comment font les gros mods](#3)
4. [Robustesse aux mises à jour du jeu](#4)
5. [Performances — le coût réel d'un patch](#5)
6. [Spécifique coop — PacketSuppressor, intention vs résultat, argent/temps](#6)
7. [Pièges Mono/Unity récapitulés](#7)
8. [Plan Harmony pour CoopAmbitions](#8)

---

<a name="1"></a>
## 1. Harmony vs HarmonyX vs MonoMod : lequel embarquer ?

### 1.1 L'état de l'écosystème en 2026

Trois couches, du bas vers le haut :

| Couche | Rôle | Qui l'utilise directement |
|---|---|---|
| **MonoMod** (v25+ : `MonoMod.Core`, `MonoMod.RuntimeDetour`, `MonoMod.Utils`…) | Le moteur de détour natif : réécrit le prologue machine d'une méthode JITée pour sauter vers un remplacement ; `ILHook` pour manipuler l'IL | SMAPI (rewriting Cecil + détours), Everest (Celeste), les deux Harmony ci-dessous |
| **Harmony « pardeike »** (Lib.Harmony, **2.4.2** en 2026) | L'API haut niveau Prefix/Postfix/Transpiler/Finalizer + annotations. **Depuis la 2.3, Harmony n'a plus son propre moteur : il est construit sur MonoMod.Core**, et le paquet NuGet `Lib.Harmony` est un « fat package » : ILRepack fusionne MonoMod et ses dépendances *dans* `0Harmony.dll` (cf. `/home/user/harmony/Lib.Harmony/Lib.Harmony.csproj` : `ProjectReference` vers `LocalMonoMod/src/MonoMod.Core` + cible `RemoveExtraDlls` qui supprime les `MonoMod.*` après pack). Cibles : `net35;net452;net472;net48;…;net10.0` | RimWorld (tout l'écosystème), Bannerlord, la majorité des mods « un jeu, un loader nu » |
| **HarmonyX** (fork BepInEx, **2.16.1**, mars 2026) | Même API que Harmony 2, réimplémentée sur `MonoMod.RuntimeDetour` ; « Unity support first » ; quelques différences de comportement (voir 1.3) | BepInEx 5/6 et tout son écosystème (Valheim/Jotunn, Lethal Company…), **Nitrox** (`Directory.Packages.props` : `HarmonyX 2.10.0`) |

**MonoMod seul** n'est jamais le bon choix pour un mod de gameplay : c'est l'outillage
de plombier (détours bruts, manipulation IL à la Cecil). On ne l'utilise que si on écrit
soi-même un loader ou un moteur de réécriture (ce que fait SMAPI). Pour poser des
dizaines de Prefix/Postfix, il faut l'API Harmony — la seule question est *laquelle des deux*.

### 1.2 Le critère décisif pour Big Ambitions : le loader « nu »

Le SDK officiel de BA charge la DLL du mod et les DLL du dossier `Dependencies/`
(dossier **plat**, `.dll` uniquement — règle 12 du `ModValidator`, cf.
`/home/user/ba-official/Assets/Editor/ModBuilder/ModValidator.cs`,
`RuleDependenciesFolderShape`). Il ne fournit **ni Harmony, ni MonoMod, ni résolution
de dépendances NuGet**. Conséquences :

- **HarmonyX 2.16.1 n'est PAS autonome** : son paquet NuGet déclare
  `MonoMod.RuntimeDetour >= 25.3.4` en dépendance → il faudrait déposer dans
  `Dependencies/` : `0Harmony.dll` + `MonoMod.RuntimeDetour.dll` + `MonoMod.Core.dll` +
  `MonoMod.Utils.dll` + `MonoMod.ILHelpers.dll` + `MonoMod.Backports.dll` (+ selon la
  cible `Mono.Cecil*.dll`). Ça marche (Nitrox expédie exactement ce bouquet via son
  launcher), mais chaque DLL supplémentaire est une occasion de collision avec un autre
  mod qui embarquerait *sa* version de MonoMod.
- **Lib.Harmony 2.4.2 « fat » est UNE seule DLL** (`0Harmony.dll`, build `net472` pour
  Unity 2022.3 Mono qui expose le profil .NET Framework 4.7.x) : MonoMod est fusionné
  dedans et internalisé. C'est le format prévu par pardeike précisément pour ce cas
  (doc `basics.md` : « Harmony can co-exist in multiple versions with itself so it is
  totally fine that each user packs their own 0Harmony.dll with their mod »).

**→ Verdict : Lib.Harmony 2.4.2, build net472, un seul fichier `0Harmony.dll` dans
`Dependencies/`.** Détails d'obtention en §8.1. HarmonyX resterait un choix défendable
(Nitrox le prouve) si BA passait un jour sous BepInEx, mais dans un loader nu la DLL
unique auto-suffisante gagne.

### 1.3 Différences de comportement Harmony ↔ HarmonyX (à connaître même si on prend Harmony)

Source : wiki HarmonyX, page « Difference between Harmony and HarmonyX »
(https://github.com/BepInEx/HarmonyX/wiki/Difference-between-Harmony-and-HarmonyX) :

- **Skip de préfixes** : chez pardeike, un Prefix qui retourne `false` saute l'original
  **et** les Prefix suivants « qui ont un effet sur l'original » (les Prefix `void` sans
  argument `ref` sont considérés sans effet de bord et tournent toujours — doc
  `execution.md`). HarmonyX exécute *tous* les Prefix quoi qu'il arrive et fournit
  `__runOriginal` pour tester. Les deux injectent `bool __runOriginal` en lecture seule.
- **Unpatch** : HarmonyX a supprimé `instance.UnpatchAll(string)` au profit de
  `instance.UnpatchSelf()` / `Harmony.UnpatchID(id)` / `Harmony.UnpatchAll()` (statique),
  parce que trop de mods appelaient `UnpatchAll()` sans argument et **dépatchaient les
  autres mods**. Chez pardeike 2.4, l'équivalent de `UnpatchSelf()` est
  `harmony.UnpatchAll(harmony.Id)` — **toujours passer son propre Id** (signature réelle
  dans `/home/user/harmony/Harmony/Public/Harmony.cs` : `public void UnpatchAll(string harmonyID = null)`).
- **Méthodes `extern`/natives** : HarmonyX sait les patcher via NativeDetour ; Harmony
  classique refuse (« method has no body ») sauf transpiler-only qui *remplace* le corps.
- **Logs** : Harmony = `Harmony.DEBUG` + `FileLog` (fichier sur le Bureau ! contrôlable
  par les variables d'env `HARMONY_NO_LOG` / `HARMONY_LOG_FILE` en 2.4) ; HarmonyX =
  `Logger.ChannelFilter` + `HarmonyFileLog.Enabled` (c'est ce que règle Nitrox dans
  `Patcher.InitPatches()` : `HarmonyFileLog.Enabled = false;`).

### 1.4 Deux mods, deux Harmony : que se passe-t-il dans un loader nu ?

Scénario réel : CoopAmbitions embarque `0Harmony.dll` 2.4.2, un autre mod BA embarque
`0Harmony.dll` 2.2 (ou HarmonyX). Ce qu'il faut savoir :

1. **Chargement** : Mono charge les deux assemblies même si elles portent le même nom
   simple `0Harmony` (contextes LoadFrom distincts, versions différentes). Chaque mod
   résout ses types Harmony contre *sa* copie. Si les deux mods embarquent **exactement
   la même version**, le runtime peut réutiliser la première chargée — bénin.
2. **Interop des patches** : Harmony maintient un **état partagé inter-assemblies**
   (`HarmonySharedState`, `/home/user/harmony/Harmony/Internal/HarmonySharedState.cs`) :
   un type créé dynamiquement dans l'AppDomain, versionné, que toutes les copies de
   Harmony 2.x consultent. C'est ce qui permet à deux copies 2.x *différentes* de voir
   les patches l'une de l'autre et de rejouer la chaîne complète (priorités comprises)
   au lieu de s'écraser. HarmonyX participe au même mécanisme (interop revendiquée avec
   MonoMod.RuntimeDetour et Harmony 2).
3. **Les vraies casses** :
   - Harmony **1.x** et 2.x sont incompatibles (API et état partagé) — plus vraiment un
     sujet en 2026, SMAPI a même un rewriter Cecil qui détecte les mods Harmony 1.x
     (`HarmonyDetector`, `/home/user/smapi/src/SMAPI/Framework/ModLoading/Rewriters/StardewValley_1_5/HarmonyRewriter.cs`).
   - Versions 2.x aux formats d'état partagé trop éloignés + ordre de chargement
     défavorable (le vieux charge après le neuf) → erreurs exotiques. C'est pour ça que
     les loaders mûrs fournissent UNE Harmony partagée : BepInEx expédie HarmonyX pour
     tout le monde ; RimWorld a un mod « Harmony » prérequis universel qui charge la
     dernière version avant tout ; SMAPI expédie son `0Harmony.dll` maison
     (`/home/user/smapi/build/0Harmony.dll`, référencé en dur par `SMAPI.csproj`) et
     interdit aux mods d'apporter le leur.
   - Deux moteurs de détour **étrangers** (ex. un mod MelonLoader-style avec vieux
     MonoMod + notre Harmony) patchant la même méthode : le dernier arrivé re-détourne
     le remplacement du premier. En pratique ça marche par empilement, mais l'Unpatch
     du premier peut casser la chaîne.
4. **Hygiène qui nous protège** (et protège les autres) :
   - Id unique en notation domaine inversé : `new Harmony("com.coopambitions.patches")`.
   - **Jamais** `UnpatchAll()` sans Id.
   - Ne pas patcher plus tôt que nécessaire (voir §7 « patch trop tôt »).
   - Diagnostic embarqué : `Harmony.VersionInfo(out var required)` liste quelles
     assemblies utilisent quelle version de Harmony (doc `basics.md`) ; à logger au boot.

### 1.5 Hot-unload : dépatcher proprement à `OnUnloadAsync`

Le SDK BA peut décharger/recharger un mod (et de toute façon on veut pouvoir couper le
mode coop en revenant au menu). Modèle éprouvé = **Nitrox**, qui sépare :

- `IPersistentPatch` : appliqués à l'init du jeu, jamais retirés (menus, désactivation
  de télémétrie…) ;
- `IDynamicPatch` : appliqués à l'entrée en session multi, retirés au retour au menu.

Code réel (abrégé) — `/home/user/nitrox/NitroxPatcher/Patcher.cs` :

```csharp
private static readonly Harmony harmony = new("com.nitroxmod.harmony");

public static void Apply()
{
    foreach (IDynamicPatch patch in container.Resolve<IDynamicPatch[]>())
    {
        try { patch.Patch(harmony); }
        catch (HarmonyException e) { /* déroule InnerException et log */ }
        catch (Exception e) { Log.Error($"Error patching {patch.GetType().Name}\n{e}"); }
    }
    isApplied = true;
}

public static void Restore()
{
    foreach (IDynamicPatch patch in container.Resolve<IDynamicPatch[]>())
        patch.Restore(harmony);
    isApplied = false;
}
// branchés sur le cycle de vie :
Multiplayer.OnBeforeMultiplayerStart += Apply;
Multiplayer.OnAfterMultiplayerEnd += Restore;
```

et chaque patch note ses cibles pour pouvoir les retirer — `NitroxPatch.cs` :

```csharp
private readonly List<MethodBase> activePatches = new();

public void Restore(Harmony harmony)
{
    foreach (MethodBase targetMethod in activePatches)
        harmony.Unpatch(targetMethod, HarmonyPatchType.All, harmony.Id);  // ← SON id
}

protected void PatchMultiple(Harmony harmony, MethodBase targetMethod, MethodInfo prefix = null, ...)
{
    Validate.NotNull(targetMethod, "Target method cannot be null");
    harmony.Patch(targetMethod, AsHarmonyMethod(prefix), ...);
    activePatches.Add(targetMethod); // Store our patched methods
}
```

Points durs à retenir sur l'unpatch (doc `basics.md`, section « Unpatching ») :

> « Once a method is patched, the original method is destroyed […] you cannot *unpatch*
> a method. You can only patch it with zero patches. »

Autrement dit : `Unpatch` **rejoue** la méthode avec les patches restants (les nôtres en
moins) — le comportement redevient l'original, mais la méthode reste « gérée » par
Harmony. C'est transparent, sauf si un autre moteur de détour est passé derrière nous.
Pour CoopAmbitions : `harmony.UnpatchAll(harmony.Id)` dans `OnUnloadAsync` suffit
(équivalent pardeike du `UnpatchSelf()` de HarmonyX), plus la remise à zéro de tout
état statique de nos classes de patch (suppresseurs, caches) — l'assembly, elle, n'est
réellement déchargeable dans aucun cas (pas d'AppDomain séparé sous Unity) ; un
« unload » BA est en réalité un désarmement.

---

<a name="2"></a>
## 2. Anatomie des patches — référentiel complet

Un patch Harmony réécrit la méthode cible en un « remplacement » qui appelle :
`Prefixes → original (transpilé) → Postfixes`, le tout éventuellement enveloppé de
try/catch si des Finalizers existent (doc `execution.md`). Détail d'exécution qui
compte : les exceptions des Prefix/Postfix **remontent à l'appelant** par défaut — d'où
l'intérêt du try/catch interne ou du Finalizer pour un mod réseau qui ne doit jamais
crasher le jeu.

### 2.1 Prefix — intercepter, enrichir, court-circuiter

Signature : `static bool|void Prefix(...)`. Retourner `false` saute l'original (et les
Prefix « à effet » suivants). Injections disponibles (doc `patching-injections.md`) :

| Injection | Rôle |
|---|---|
| `__instance` | le `this` (méthode d'instance) |
| `ref T __result` | lire/écrire la valeur de retour (dans un Prefix : à écrire si on retourne `false`) |
| `T __state` / `ref` | variable locale passée du Prefix au Postfix **de la même classe** |
| `___champPrivé` (3 underscores) | accès direct à un champ privé par nom (`ref` pour écrire) |
| `object[] __args` | tous les arguments (édition = répercutée) ; léger surcoût |
| `nomArgument` ou `__0`, `__1` | les arguments de l'original, par nom ou par index |
| `MethodBase __originalMethod` | quelle méthode on patch (utile avec `TargetMethods`) — **ne pas l'invoquer** |
| `bool __runOriginal` | l'original va-t-il / a-t-il tourné (lecture seule) |

**Court-circuit complet avec `__result`** — le patch « intention » type de Nitrox
(`/home/user/nitrox/NitroxPatcher/Patches/Dynamic/Bed_EnterInUseMode_Patch.cs`) :
en multi on ne dort pas tout de suite, on prévient le réseau et on attend les autres :

```csharp
public sealed partial class Bed_EnterInUseMode_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD =
        Reflect.Method((Bed t) => t.EnterInUseMode(default(Player)));

    public static bool Prefix(Bed __instance, Player player)
    {
        if (__instance.inUseMode != Bed.InUseMode.None) return false;

        player.FreezeStats();                       // on refait NOUS la partie voulue
        __instance.inUseMode = Bed.InUseMode.Sleeping;

        Resolve<IPacketSender>().Send(new BedEnter());   // intention → réseau
        Resolve<SleepManager>().EnterBed(__instance);

        return false;                                // l'original ne tourne PAS
    }
}
```

Le commentaire de tête du fichier explique le choix : *« Uses Prefix instead of
Transpiler because we need to prevent the original method from starting the sleep
animation — in multiplayer we wait for all players before sleeping. »* C'est LE
critère : **si le comportement solo doit être remplacé, Prefix + `return false` ; s'il
doit être observé, Postfix ; s'il doit être modifié chirurgicalement au milieu,
Transpiler.**

Pour une méthode à retour, le court-circuit doit poser le résultat :

```csharp
// Exemple canonique (doc patching-prefix.md, adapté)
public static bool Prefix(ref decimal __result)
{
    __result = 0m;      // ce que verront les appelants
    return false;       // skip original
}
```

### 2.2 Postfix — observer et répliquer

Toujours exécuté (sauf exception non-finalisée en amont). Peut lire/modifier
`__result`, y compris de façon « pass-through ». Exemple réel Combat Extended
(`/home/user/combatextended/Source/CombatExtended/Harmony/Harmony_Thing.cs`) — ajoute
les munitions du chargeur aux produits de fonte d'une arme :

```csharp
[HarmonyPatch(typeof(Thing), "SmeltProducts")]
public class Harmony_Thing_SmeltProducts
{
    public static void Postfix(Thing __instance, ref IEnumerable<Thing> __result)
    {
        var ammoUser = (__instance as ThingWithComps)?.TryGetComp<CompAmmoUser>();
        if (ammoUser != null && ammoUser.HasMagazine && ammoUser.CurMagCount > 0 && ammoUser.CurrentAmmo != null)
        {
            var ammoThing = ThingMaker.MakeThing(ammoUser.CurrentAmmo, null);
            ammoThing.stackCount = ammoUser.CurMagCount;
            __result = __result.AddItem(ammoThing);
        }
    }
}
```

Et l'exemple « réplication d'un résultat » minimal de Nitrox
(`BeaconLabel_SetLabel_Patch.cs`) : un Postfix qui broadcast la mutation une fois
qu'elle a eu lieu localement.

### 2.3 `__state` : mesurer un delta autour de l'original

Pattern crucial pour le coop (delta d'argent, delta de stock) :

```csharp
// Notre futur patch d'inventaire, pattern __state (doc patching-injections.md)
public static void Prefix(WarehouseSection __instance, out int __state)
    => __state = __instance.StockCount;               // avant

public static void Postfix(WarehouseSection __instance, int __state)
{
    int delta = __instance.StockCount - __state;      // après - avant
    if (delta != 0) CoopEvents.OnStockDelta(__instance, delta);
}
```

### 2.4 Propriétés, champs privés, constructeurs

**Setter de propriété** — deux voies. Par annotation (Combat Extended,
`Harmony_Thing.cs`) :

```csharp
[HarmonyPatch(typeof(Thing), nameof(Thing.Position), MethodType.Setter)]
[HarmonyPriority(Priority.First)]
public class Harmony_Thing_Position
{
    private static FieldInfo fPosition = AccessTools.Field(typeof(Thing), "positionInt");
    // ... transpiler qui insère un callback juste après `positionInt = value;`
}
```

Par réflexion typée (Nitrox, `Battery_charge_set_Patch.cs`) — noter le **throttling
dans le patch** (broadcast seulement au changement d'entier, pas à chaque frame) :

```csharp
public static readonly MethodInfo TARGET_METHOD =
    Reflect.Property((Battery t) => t.charge).SetMethod;

public static void Prefix(Battery __instance, float value)
{
    if (Math.Abs(Math.Floor(__instance.charge) - Math.Floor(value)) > 0.0 &&
        __instance.TryGetIdOrWarn(out NitroxId id))
    {
        Resolve<Entities>().EntityMetadataChanged(__instance, id);
    }
}
```

**AccessTools** (doc `utilities.md`) — la boîte à outils réflexion de Harmony, avec
`BindingFlags` « tout » par défaut et **retour `null` silencieux** si introuvable
(fondement de la dégradation gracieuse, §4) :

```csharp
var m  = AccessTools.Method(typeof(GameManager), "ChangeMoneySafe",
                            new[] { typeof(decimal), typeof(TransactionInfo), typeof(bool) });
var m2 = AccessTools.Method("GameManager:ChangeMoneySafe");   // notation "Type:méthode"
var f  = AccessTools.Field(typeof(SaveGameManager), "current");
var p  = AccessTools.PropertyGetter(typeof(SaveGameManager), "Current");
var c  = AccessTools.Constructor(typeof(Timestamp), new[] { typeof(int), typeof(int), typeof(float) });
// + FieldRefAccess<T,F>() : accès champ privé compilé (rapide, pour les hot paths)
var chargeRef = AccessTools.FieldRefAccess<Battery, float>("_charge");
chargeRef(battery) = 100f;
```

**Constructeurs** : patchables comme des méthodes (`MethodType.Constructor` +
`argumentTypes`, ou `AccessTools.Constructor`). Deux pièges (doc
`patching-edgecases.md`) : on ne peut **pas** changer le type retourné par `newobj`
(le ctor n'est qu'un initialiseur) ; et les **constructeurs statiques** sont
pratiquement impatchables (déjà exécutés dès que Harmony touche le type, et les
repatcher les ferait retourner).

### 2.5 Transpiler — quand, et comment ne pas le regretter

**Quand** : quand ni Prefix ni Postfix ne suffisent — typiquement « injecter un appel
AU MILIEU d'une grosse méthode, au point précis où l'objet intéressant existe », ou
« remplacer UN appel interne par le nôtre ». Nitrox s'en sert massivement ; Combat
Extended aussi. **Quand pas** : dès qu'un Prefix+`__state`+Postfix donne le même
résultat — un transpiler casse à chaque changement d'IL de la cible, un Prefix survit
à presque tout.

Voie moderne : **CodeMatcher** (doc `patching-transpiler-matcher.md`, exemple recopié de
`/home/user/harmony/Documentation/examples/patching-transpiler-codematcher.cs`) :

```csharp
static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
{
    var codeMatcher = new CodeMatcher(instructions /*, ILGenerator generator*/);

    codeMatcher.MatchStartForward(
            CodeMatch.Calls(() => default(DamageHandler).Kill(default))
        )
        .ThrowIfInvalid("Could not find call to DamageHandler.Kill")
        .RemoveInstruction()
        .InsertAndAdvance(
            CodeInstruction.Call(() => MyDeathHandler(default, default))
        );

    return codeMatcher.Instructions();
}
```

`ThrowIfInvalid` est capital : un transpiler dont le motif ne matche plus doit
**échouer bruyamment au patch** (et être attrapé par notre try/catch de chargement),
pas produire silencieusement l'IL original.

Nitrox a poussé plus loin avec sa mini-lib `PatternMatching`
(`/home/user/nitrox/NitroxPatcher/PatternMatching/`) : un motif déclaratif + labels,
et l'insertion se fait « après le marqueur ». Exemple réel complet
(`SpawnOnKill_OnKill_Patch.cs`) — injection d'un callback juste après le
`Object.Instantiate` interne, pour capturer l'objet spawné :

```csharp
private static readonly InstructionsPattern spawnInstanceOnKillPattern = new()
{
    Reflect.Method(() => UnityEngine.Object.Instantiate(default(GameObject), default(Vector3), default(Quaternion))),
    { Stloc_0, "DropOnKillInstance" },
    Ldarg_0,
};

public static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> instructions)
{
    return instructions.InsertAfterMarker(spawnInstanceOnKillPattern, "DropOnKillInstance", new CodeInstruction[]
    {
        new(Ldarg_0),
        new(Ldloc_0),
        new(Call, ((Action<SpawnOnKill, GameObject>)Callback).Method)
    });
}

private static void Callback(SpawnOnKill spawnOnKill, GameObject spawningItem)
{
    // ... envoi du packet EntityDestroyed + enregistrement de l'objet spawné
}
```

Règle d'or visible dans TOUT le code Nitrox/CE : **le transpiler n'injecte qu'un
`call` vers une méthode statique C# du patch** (« trampoline »). Zéro logique en IL
brut — l'IL localise, le C# décide. Autre exemple Nitrox : remplacer l'operand d'un
`call` pour rerouter un appel (`ToggleLights_SetLightsActive_Patch.cs`,
`instruction.operand = Reflect.Method(() => PlayEnvSound3D(...))`).

### 2.6 Finalizer — le try/finally garanti

S'exécute même si l'original ou un patch lève. Reçoit/retourne `Exception __exception`
(retourner `null` = avaler l'exception ; la retourner = la propager). Usage réel chez
Nitrox : garantir qu'un suppresseur est bien relâché même si l'original explose
(`PacketSuppressorPatch.cs`, code complet en §6.1). Attention : le premier Finalizer
sur une méthode fait basculer tout le remplacement en structure try/catch (léger coût,
§5).

### 2.7 Génériques : les pièges (doc `patching-edgecases.md`)

- Les méthodes génériques et méthodes de classes génériques sont **partagées entre les
  `T` référence** au runtime : patcher `Method<object>` patche pour tous les types
  référence… et peut « écraser » l'identité générique (« if `Method<T>` is patched
  using `Method<string>`, `Method<object>` will become `Method<string>` »).
- Les instanciations **value type** (`int`, structs) ont chacune leur code natif :
  il faut les patcher une par une (et elles seules sont sûres).
- Contournement pour méthode non-générique d'une classe générique : patcher via une
  instanciation concrète et discriminer dans le patch avec
  `__instance.GetType().GenericTypeArguments`.
- **Conclusion pratique CoopAmbitions : ne jamais patcher une méthode générique si un
  appelant non-générique existe plus haut.** (Combat Extended fait exactement ça : pour
  `ThingOwner<T>`, il patche les méthodes **non génériques de la base `ThingOwner`** —
  `HarmonyBase.PatchThingOwner()`.)

### 2.8 Inlining : LE piège Mono classique

Une méthode courte (petit getter, `AddMoney` d'une ligne…) peut être **inlinée** par le
JIT dans ses appelants : le détour est bien posé sur la méthode, mais plus personne ne
l'appelle. Symptôme : patch appliqué sans erreur, jamais exécuté.

Ce que disent les sources de référence (wiki HarmonyX « Valid patch targets » +
doc Harmony `patching-edgecases.md`) :

- Sur **Mono** — notre cas — MonoMod/Harmony **désactivent l'inlining de la cible**,
  *à condition de patcher avant que la méthode ait été JITée dans un appelant* :
  « on Mono, Harmony automatically disables inlining, provided you patch the short
  method before it is called anywhere ».
- Sur .NET Framework/Core : pas de correctif fiable (« there is no concrete fix at the
  moment ») — d'où le `TieredCompilation=false` de SMAPI (`SMAPI.csproj` : commentaire
  « tiered compilation breaks Harmony ») ; non applicable à Mono mais à retenir si BA
  migrait un jour vers CoreCLR.

Recette CoopAmbitions :

1. **Patcher tôt** les petites méthodes chaudes (au `[ModEntryOnInitializationLoad]`,
   avant que le gameplay ait tourné) — nos patches « persistants » ; les patches de
   session (dynamiques) ne visent que des méthodes plus grosses, jamais des getters.
2. **Détection** : au premier déclenchement attendu, vérifier qu'un compteur du patch a
   bougé (smoke test comportemental, §4.3). Un patch de getter jamais appelé = suspect
   d'inlining.
3. **Contournement** si une cible est déjà inlinée : patcher **les appelants** (les
   trouver dans dnSpy « Analyze → Used By », puis `TargetMethods()` pour les patcher en
   masse — recommandation explicite de la doc : « mass-patching all occurances […]
   `TargetMethods()` is your friend »), ou patcher plus haut dans la chaîne d'appel.

### 2.9 Méthodes Unity (`Update`, `Awake`…), événements, cas restants

- `Update`/`FixedUpdate`/`Awake` sont des méthodes managées ordinaires : patchables
  normalement (Nitrox patche des dizaines d'`Update` — `Bed_Update_Patch`,
  `ArmsController_Update_Patch`…). Le « magic calling » d'Unity ne gêne pas. Voir §5
  pour le coût.
- Les **méthodes `extern` Unity** (`[MethodImpl(InternalCall)]`, ex.
  `Object.DontDestroyOnLoad`) ne sont pas patchables avec Lib.Harmony (pas d'IL) ;
  et **patcher trop tôt une méthode qui APPELLE de l'extern** jette
  `MissingMethodException: Attempted to access a missing method` tant qu'Unity n'a pas
  fini de lier ses binaires — attendre la première scène (`SceneManager.sceneLoaded`)
  pour ces cibles (doc `patching-edgecases.md`). Pour BA : patcher depuis
  `[ModEntryOnMainMenuLoad]`/`[ModEntryOnCityLoad]` nous met naturellement après cette
  phase ; seul `[ModEntryOnInitializationLoad]` mérite prudence.
- **Événements C#** : `add_X`/`remove_X` sont des méthodes patchables
  (`AccessTools.Method(typeof(T), "add_onNewDay")`), mais pour *écouter* il est
  toujours plus propre de s'abonner (`GlobalEvents.onNewDay += ...`) — patcher un
  événement ne se justifie que pour **supprimer** des abonnés du jeu ou intercepter
  le déclenchement.
- **`base.SomeMethod()` depuis un patch** : impossible par réflexion classique (la
  résolution virtuelle ramène l'override). Solution : **Reverse Patch** — copier
  l'original (éventuellement la version de base) dans un stub à soi
  (doc `reverse-patching.md`, `[HarmonyReversePatch]` + `Transpiler` optionnel).
- **Corps « dead code »** (`throw new NotImplementedException()` sans `ret`) : sous
  Mono, patcher une telle méthode jette `InvalidProgramException` — il faut un
  transpiler qui émet un corps valide.
- **Méthodes async** : patcher `MoveNext` de la machine d'état
  (`AccessTools.EnumeratorMoveNext` pour les coroutines/IEnumerator ; Nitrox patche
  `...Async` via ses helpers). Un Postfix sur la méthode async elle-même s'exécute au
  premier `await`, pas à la fin — piège connu.

---

<a name="3"></a>
## 3. Organisation à grande échelle

### 3.1 Le pattern Nitrox : un fichier = un patch = une cible

`NitroxPatcher/Patches/{Persistent,Dynamic}/` contient **~370 fichiers** nommés
`Type_Méthode_Patch.cs`. Chaque classe :

- hérite de `NitroxPatch` et marque `IPersistentPatch` **ou** `IDynamicPatch` ;
- expose `public static readonly MethodInfo TARGET_METHOD` résolu par **expression
  lambda compilée** — le pattern `Reflect.Method` ;
- contient uniquement des méthodes statiques `Prefix`/`Postfix`/`Transpiler`/`Finalizer` ;
- est instanciée par DI (Autofac scanne l'assembly : `NitroxPatchesModule.cs`,
  `RegisterAssemblyTypes(...).AssignableTo<IDynamicPatch>()`), puis `Patch(harmony)`
  est appelé **manuellement** — pas de `PatchAll`.

Le cœur refactoring-safe, `Nitrox.Model/Helper/Reflect.cs` :

```csharp
public static MethodInfo Method<T>(Expression<Action<T>> expression)
    => (MethodInfo)GetMemberInfo(expression, typeof(T));

public static PropertyInfo Property<T>(Expression<Func<T, object>> expression)
    => (PropertyInfo)GetMemberInfo(expression);
// usage :  Reflect.Method((Bed t) => t.EnterInUseMode(default(Player)))
//          Reflect.Property((Battery t) => t.charge).SetMethod
```

Pourquoi c'est supérieur aux chaînes : **la compilation échoue** si la méthode du jeu
disparaît ou change de signature (on compile contre les DLL du jeu). Une MAJ du jeu se
détecte à la recompilation, pas chez l'utilisateur. Limite : ne marche que pour les
membres `public` (BA est peu obfusqué et largement public — sinon repli `AccessTools`).

**À copier tel quel pour CoopAmbitions.** Nos avantages identiques : le SDK BA nous
fait déjà compiler contre les 32 DLL canoniques du jeu.

### 3.2 Le pattern RimWorld/Combat Extended : annotations + `PatchAll` + rattrapage manuel

CE (105 fichiers `Harmony_*.cs`) fait l'inverse : classes annotées
`[HarmonyPatch(typeof(X), "Y")]` et un unique
`instance.PatchAll(Assembly.GetExecutingAssembly())` au boot
(`HarmonyBase.InitPatches()`), complété par :

- des **patches manuels en masse** pour les hiérarchies : `PatchThingOwner()` patche 4
  méthodes non-génériques de la classe de base ; `PatchHediffWithComps` itère
  `baseType.AllSubclassesNonAbstract()` et patche chaque override ;
- des **patches différés** : `LongEventHandler.ExecuteWhenFinished(() => { if (TypeOfBGHUtils == null) return; instance.Patch(AccessTools.Method(...)); })`
  — le patch de compat ne s'applique que si le mod tiers est présent, et après le
  chargement complet ;
- un **singleton Harmony paresseux** avec le commentaire : *« One should only have a
  single instance of Harmony per Assembly. »*

`PatchAll` vs manuel — arbitrage pour nous :

| | `PatchAll` + annotations | Manuel (Nitrox) |
|---|---|---|
| Verbosité | minimale | +8 lignes/patch |
| Cible refactoring-safe | non (chaînes `nameof` au mieux) | oui (`Reflect.Method`) |
| Un patch cassé au chargement | **fait échouer tout le `PatchAll`** (une exception interrompt la boucle) — sauf à éclater par catégorie | isolé par try/catch par patch |
| Patches dynamiques (session coop) | via `PatchCategory` | natif (liste d'objets) |
| Introspection/smoke test | réflexion sur les attributs | trivial (on tient la liste) |

**Verdict : manuel façon Nitrox.** Pour un mod coop, « un patch qui rate ne doit pas
empêcher les 40 autres » et « désarmer à la fin de session » sont non négociables.
Note : Harmony 2.2+ offre un entre-deux — `[HarmonyPatchCategory("session")]` +
`harmony.PatchCategory("session")` / `harmony.UnpatchCategory("session")` — mais on
perd l'isolation d'erreur par patch, donc on n'en a pas besoin.

### 3.3 État partagé entre patches

- Nitrox : **zéro état dans les classes de patch** (hors caches statiques et
  suppresseurs) ; toute la logique vit dans des services résolus par
  `Resolve<T>()` (cache du service locator, `NitroxPatch.Resolve<T>`). Les patches
  sont une couche d'adaptation ultra-mince entre le jeu et `NitroxClient.GameLogic`.
- CE : état statique par classe de patch (ex. `FieldInfo` mis en cache) + services
  singleton du mod.
- **Pour nous** : les patches appellent des façades (`CoopMoney`, `CoopTime`,
  `CoopPlacement`) qui vivent dans l'assembly principal du mod ; les patches ne
  connaissent ni le transport ni la sérialisation. Ça isole aussi les accès au jeu
  par domaine (exigence déjà actée dans SYNTHESE §1).

### 3.4 Priorités et cohabitation

Annotations utilisables aussi en manuel via `HarmonyMethod { priority, before, after }`
(doc `priorities.md`) : `[HarmonyPriority(Priority.First/Last/…)]`,
`[HarmonyBefore("autre.id")]`, `[HarmonyAfter("autre.id")]`. CE s'en sert
(`Priority.First` sur le setter `Thing.Position` pour voir la valeur avant les autres
mods). Pour CoopAmbitions : mettre `Priority.First` sur les Prefix suppresseurs
(il faut supprimer AVANT qu'un autre patch n'observe), `Priority.Last` sur les Postfix
de réplication (répliquer l'état final, après les autres mods).

---

<a name="4"></a>
## 4. Robustesse aux mises à jour du jeu

La 1.0 de BA est sortie le 28/08/2026, updates + DLC annoncés ; chaque MAJ Steam
invalide potentiellement nos cibles. Stratégie en profondeur, du plus au moins
automatique :

### 4.1 Échec de compilation = détection gratuite (pattern Reflect)

Tout ce qui passe par `Reflect.Method((GameManager g) => g.ChangeMoneySafe(...))`
casse **à la recompilation** après import des nouvelles DLL. C'est un *feature* : la
liste des patches à réviser tombe toute seule. (Les cibles privées via `AccessTools`
n'ont pas ce filet — les minimiser.)

### 4.2 Au runtime : null-check + try/catch PAR patch + rapport

Trois mécanismes complémentaires, tous vus dans le code étudié :

1. `AccessTools.*` retourne `null` sans jeter → chaque résolution douteuse est
   vérifiée (Nitrox : `Validate.NotNull(targetMethod, "Target method cannot be null")`
   dans `PatchMultiple` — l'exception est ensuite attrapée par patch).
2. La boucle d'application attrape **par patch** (`Patcher.Apply()`, §1.5) : un patch
   mort = une ligne d'erreur + le reste du mod fonctionne. Pour `HarmonyException`,
   Nitrox déroule les `InnerException` jusqu'à la racine avant de logger — à copier,
   les erreurs de transpiler sont sinon illisibles.
3. CE conditionne des patches à la présence d'un type
   (`if (TypeOfBGHUtils == null) return;`) — même idée pour nos patches sur des types
   BA « incertains » (ex. tout ce qu'on n'a pas encore confirmé dans dnSpy).

### 4.3 Smoke tests au chargement (et en CI)

Deux étages :

- **Étage CI (offline), pattern Nitrox** : `Nitrox.Test/Patcher/PatchesTranspilerTest.cs`
  référence CHAQUE patch transpiler avec le **delta d'instructions attendu**
  (`[typeof(SpawnOnKill_OnKill_Patch), 3]` = le transpiler doit ajouter 3
  instructions) ; le test lit l'IL réel de la cible
  (`PatchProcessor.ReadMethodBody`), applique le transpiler et compare. Une MAJ du jeu
  qui change l'IL fait rougir la CI avant tout runtime. On fera pareil dès notre
  premier transpiler (les DLL BA sont importables dans un projet de test).
- **Étage chargement (in-game)** : après `ApplyAll()`, logger un rapport
  `patches: 38 ok, 2 failed [X_Patch: cible introuvable, Y_Patch: motif transpiler]`
  et l'exposer à l'UI coop. Décision produit à câbler dedans : certains patches sont
  **critiques** (argent, temps → refuser d'héberger/joindre si absents), d'autres
  **dégradables** (cosmétique → warning). Le squelette §8 implémente
  `PatchCriticality`.

### 4.4 Détection de version et patches alternatifs

- Lire `Application.version` + le buildid Steam que le SDK trace déjà (SYNTHESE §1) ;
  logger les deux dans le rapport de patch.
- Pattern « compat par capacité » plutôt que par numéro (plus robuste) : tester
  l'existence de la *nouvelle* signature puis de l'ancienne, et brancher le bon patch —
  c'est ce que fait l'écosystème RimWorld entre 1.4/1.5 (CE a des
  `Harmony_BackCompatibility_1_5.cs`) :

```csharp
// Pattern cible : résolution multi-signatures avec repli
MethodBase target =
    AccessTools.Method(typeof(GameManager), "ChangeMoneySafe",
        new[] { typeof(decimal), typeof(TransactionInfo), typeof(bool) })
 ?? AccessTools.Method(typeof(GameManager), "ChangeMoneySafe",
        new[] { typeof(float), typeof(TransactionInfo), typeof(bool) })
 ?? AccessTools.FirstMethod(typeof(GameManager), m => m.Name.StartsWith("ChangeMoney"));
if (target == null) report.Fail(this, "ChangeMoney* introuvable — MAJ du jeu ?");
```

- Enfin, le **handshake réseau versionné** (déjà codé chez nous) doit inclure le
  buildid du jeu ET le hash de la liste des patches actifs : deux joueurs avec des
  patches différents ne doivent pas se connecter.

---

<a name="5"></a>
## 5. Performances — le coût réel d'un patch

### 5.1 Mécanique et ordres de grandeur

Après patch, la cible est recompilée en un « remplacement » ; l'original détourne vers
lui par un saut natif. Coûts :

- **Au patch** (une fois) : cher — lecture d'IL, émission DynamicMethod, JIT. Quelques
  ms par méthode ; 50 patches = imperceptible au chargement, mais ne pas patcher/
  dépatcher en boucle pendant le jeu.
- **À l'appel** : le remplacement ajoute par patch un appel statique + le marshalling
  des injections demandées. Un Prefix/Postfix trivial ≈ un appel de méthode non-inliné
  (dizaines de ns). Ça ne se voit que sur des méthodes appelées des milliers de fois
  par frame (la doc communautaire — ex. guide Reactor — dit la même chose : surcoût
  minime sauf hot paths type boucle physique).
- **Ce qui coûte vraiment** dans un patch chaud : `object[] __args` (alloc par appel),
  les injections `ref struct` volumineuses, les **allocations dans NOTRE code**
  (closures, LINQ, string interpolation de logs !), et le Finalizer (bascule tout le
  remplacement en try/catch).
- **Transpiler** : surcoût d'exécution ≈ zéro — c'est du code inséré une fois, JITé
  normalement. C'est LA réponse « perf » pour instrumenter une méthode chaude en un
  point précis plutôt qu'un Prefix appelé à chaque entrée.

### 5.2 Doctrine sur les méthodes chaudes (`Update` & co)

Nitrox patche des `Update` quand il le faut, mais regarde comment :
`Battery_charge_set_Patch` filtre au **changement d'entier** avant de rien faire ;
`Bullet_Update_Patch` et autres sont des transpilers qui insèrent un appel au point
utile plutôt qu'un Prefix systématique. Doctrine CoopAmbitions :

1. **Un événement du jeu existe ? On s'abonne, on ne patche pas.** BA expose
   `GlobalEvents.onNewDay/onNewHour/onEnterBuilding/onVehicleVariablesChanged` +
   `RegisterOnGameLoadedCallback` — couvrent déjà temps et transitions.
2. **Boucle propre à nous ? Un MonoBehaviour à nous** (pattern Nitrox :
   `nitroxRoot.AddComponent<NitroxBootstrapper>()` dans `Patcher.ApplyNitroxBehaviours`)
   — l'émission des positions avatar à 10-20 Hz vit dans notre component, jamais dans
   un patch d'`Update` du jeu.
3. **Mutation ponctuelle du jeu ? Patch sur la méthode de mutation** (achat, placement,
   embauche) — fréquence humaine, coût nul.
4. **Point chaud sans événement ?** Transpiler chirurgical + early-out le plus tôt
   possible dans le callback (`if (!CoopSession.Active) return;` en première ligne).
5. Jamais de log non conditionnel dans un patch chaud.

---

<a name="6"></a>
## 6. Spécifique coop

### 6.1 Le PacketSuppressor de Nitrox, en entier

Problème : quand on **applique** une mutation reçue du réseau, nos propres patches
émetteurs se déclenchent → écho infini. Solution Nitrox : un drapeau par TYPE de
packet, RAII, consulté par l'émetteur.

Code réel complet — `/home/user/nitrox/NitroxClient/Communication/PacketSuppressor.cs` :

```csharp
/// <summary>
///     Suppresses the given packet type from being sent. Disables the suppression when disposed.
/// </summary>
public readonly struct PacketSuppressor<T> : IDisposable where T : Packet
{
    private static int suppressCount;
    public static bool IsSuppressed => suppressCount > 0;

    private static readonly PacketSuppressor<T> instance = new();

    public static PacketSuppressor<T> Suppress()
    {
        suppressCount++;
        return instance;
    }

    public void Dispose()
    {
        suppressCount--;
    }
}
```

Lecture attentive — tout est signifiant :

- **Un champ statique PAR instanciation générique** : `PacketSuppressor<MoneyChanged>`
  et `PacketSuppressor<TimeChange>` ont chacun leur `suppressCount`. Le type générique
  *est* la clé du dictionnaire, résolue à la compilation. Granularité fine gratuite.
- **Compteur, pas booléen** → réentrant : deux suppressions imbriquées (un handler réseau
  qui déclenche une mutation qui re-supprime) se composent sans se marcher dessus.
- **`readonly struct` + instance statique** : `Suppress()` n'alloue pas. Utilisable
  dans un hot path.
- **Thread-safety : il n'y en a PAS** (`suppressCount++` non atomique, non volatile).
  C'est un choix assumé : tout le gameplay Unity est monothread ; Nitrox applique le
  distant sur le main thread (queue de traitement des packets). **Règle absolue à
  hériter : le suppresseur ne se touche que depuis le main thread Unity.** Notre couche
  réseau (threads Steam callbacks) poste vers une file drainée par notre MonoBehaviour ;
  si un jour un suppresseur devait être multi-thread, passer à
  `[ThreadStatic]`-par-champ (suppression scopée au thread) — PAS à `Interlocked`, qui
  rendrait la suppression visible d'un thread à l'autre (faux positifs).
- La variante `PacketSuppressor<T1..T5>` compose cinq suppressions en un `using`.

**Côté émission**, le vérificateur est dans le `PacketSender` (un seul point de sortie
réseau — le drapeau est consulté là, pas dans chaque patch) ; et côté application du
distant, les processors font :

```csharp
// /home/user/nitrox/NitroxClient/GameLogic/Vehicles.cs (réel)
public void BroadcastDestroyedVehicle(NitroxId id)
{
    using (PacketSuppressor<VehicleOnPilotModeChanged>.Suppress())
    {
        EntityDestroyed entityDestroyed = new(id);
        packetSender.Send(entityDestroyed);
    }
}
```

Et le cas « suppression longue durée » (pas de `using` possible — l'animation de
docking prend du temps) montre la version manuelle + coroutine de libération
(`Vehicles.EngagePlayerMovementSuppressor`, avec un commentaire détaillé sur les
paquets de mouvement mal datés pendant le docking).

**Suppression pilotée par patch** : quand c'est une MÉTHODE DU JEU entière qui doit
tourner « en silence », Nitrox a une classe de patch générique — Prefix qui arme,
**Finalizer** qui désarme même en cas d'exception, et garde de réentrance
(`/home/user/nitrox/NitroxPatcher/Patches/PacketSuppressorPatch.cs`, complet) :

```csharp
public abstract class PacketSuppressorPatch<T> : NitroxPatch where T : Packet
{
    public abstract MethodInfo TARGET_METHOD { get; }
    private static PacketSuppressor<T> packetSuppressor;
    private static bool wasSuppressed;

    public static void Prefix()
    {
        wasSuppressed = PacketSuppressor<T>.IsSuppressed;
        packetSuppressor = PacketSuppressor<T>.Suppress();
    }

    public static void Finalizer()
    {
        if (!wasSuppressed)
        {
            packetSuppressor.Dispose();
        }
    }

    public override void Patch(Harmony harmony)
    {
        PatchPrefix(harmony, TARGET_METHOD, ((Action)Prefix).Method);
        PatchFinalizer(harmony, TARGET_METHOD, ((Action)Finalizer).Method);
    }
}
```

*(Note : la garde `wasSuppressed` de ce code protège de la double-libération mais pas
d'une vraie récursion de la cible ; pour nos patches on gardera le compteur nu +
`Dispose()` systématique dans le Finalizer, plus simple à raisonner.)*

### 6.2 Patches « intention » vs patches « résultat »

Les deux familles, avec leurs marqueurs dans le code étudié :

| | Patch **intention** (avant l'action) | Patch **résultat** (après l'action) |
|---|---|---|
| Forme | **Prefix**, souvent `return false` | **Postfix** (ou transpiler au point de mutation) |
| Qui | l'**invité** (et l'hôte pour les actions à valider) | l'**hôte** (source d'autorité) |
| Sens | « je VEUX faire X » → packet vers l'hôte, l'action locale n'a pas lieu (ou a lieu en optimiste) | « X a EU lieu » → broadcast pour réplication |
| Exemple réel | `Bed_EnterInUseMode_Patch` (§2.1) : `return false`, packet `BedEnter`, on attend le monde | `Battery_charge_set_Patch` (§2.4) : la charge a changé, on broadcast le metadata |
| Échec | l'hôte refuse → rien à annuler localement (rien n'a eu lieu) | n/a — le résultat est un fait |

Règle de choix pour un modèle hôte-autoritaire :

- Mutation **conflictuelle** (argent partagé, achat d'un bâtiment unique, placement sur
  une case) → **intention chez l'invité** : Prefix `return false` + packet
  `RequestX`. L'hôte exécute la vraie méthode du jeu, son patch **résultat** broadcast,
  l'invité applique sous suppresseur. Latence visible ? Ajouter un effet optimiste
  *cosmétique* uniquement (son, animation), jamais la mutation d'état.
- Mutation **non conflictuelle et locale** (position de l'avatar, anim) → pas de patch
  du tout : notre MonoBehaviour échantillonne et envoie.
- Événement **hôte-seulement** (tick économique, spawn de clients) → patch résultat
  chez l'hôte + **patch neutralisant** chez l'invité (Prefix `return false`
  inconditionnel côté invité : la simulation ne tourne pas chez lui).

### 6.3 Argent et temps : le plan de patch hôte-autoritaire concret

**Argent** — cible confirmée : `GameManager.ChangeMoneySafe(montant, TransactionInfo, bool)`
(rapport internals §3 ; utilisée par les mods Dudeldups). Un seul point de mutation =
situation rêvée :

```csharp
// CoopAmbitions — patch d'intention/autorité sur l'argent (squelette cible)
public sealed class GameManager_ChangeMoneySafe_Patch : CoopPatch
{
    public override MethodBase Target => Reflect.Method(
        (GameManager g) => g.ChangeMoneySafe(default, default, default));

    [HarmonyPriority(Priority.First)]
    public static bool Prefix(decimal amount, TransactionInfo info, bool showNotification)
    {
        if (!CoopSession.Active) return true;               // solo : transparent
        if (Suppressor<MoneyChange>.IsSuppressed) return true; // on applique du distant : laisser passer SANS réémettre

        if (CoopSession.IsHost) return true;                // l'hôte mute pour de vrai (le Postfix broadcast)

        // Invité : intention → hôte, pas de mutation locale
        CoopNet.SendToHost(new MoneyChangeRequest(amount, TransactionKind.From(info)));
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(decimal amount, TransactionInfo info, bool __runOriginal)
    {
        if (!CoopSession.Active || !CoopSession.IsHost || !__runOriginal) return;
        if (Suppressor<MoneyChange>.IsSuppressed) return;
        // Résultat d'autorité : nouveau solde lu à la source de vérité
        CoopNet.Broadcast(new MoneyChanged(SaveGameManager.Current.Money, amount, TransactionKind.From(info)));
    }
}

// Application d'un MoneyChanged reçu (chez l'invité) :
using (Suppressor<MoneyChange>.Suppress())
{
    GameManager.ChangeMoneySafe(delta, RemoteTransactionInfo(msg), showNotification: msg.Notify);
    // + snapshot correctif : si |Money local - msg.NewBalance| > ε → écrasement direct
}
```

Détails qui comptent : (1) le Postfix broadcast **le nouveau solde absolu** en plus du
delta — c'est le snapshot correctif anti-dérive de l'architecture (SYNTHESE §4.3) ;
(2) `__runOriginal` évite de broadcaster si un autre patch a annulé l'original ;
(3) chez l'invité on rappelle la VRAIE méthode du jeu sous suppresseur (notifications,
compta `Transactions`, UI restent cohérentes) au lieu d'écrire `Money` à la main.

**Temps** — cibles à confirmer dans dnSpy (singleton `DayNightCycle` qui incrémente
`Timestamp`, méthodes pause/vitesse) :

- Invité : Prefix `return false` sur le **tick** local du temps (la machine à temps ne
  tourne pas) ; l'horloge s'écrit par messages `TimeSync` de l'hôte, appliqués sous
  `Suppressor<TimeSync>` en poussant `Day/Hour/Minute`.
- Tous : Prefix `return false` sur pause / changement de vitesse local en session
  (remplacé par un vote → l'hôte décide, cf. SYNTHESE §4.6). Les émetteurs
  `onNewHour/onNewDay` restent naturels chez l'hôte, et chez l'invité se déclenchent
  quand la synchro fait franchir l'heure — les ticks planifiés (livraisons, imports)
  ne tournent de toute façon que chez l'hôte (patch neutralisant).

### 6.4 Initial sync : le silence radio

Pattern Nitrox (repris dans SYNTHESE §4.5) : pendant le join d'un invité, le monde est
muet — ne pas inventer un mécanisme séparé : c'est une **suppression globale** :
`Suppressor<AllPackets>.Suppress()` (un type marqueur dont `PacketSender.Send` teste le
compteur en premier) tenue pendant tout le traitement des processeurs d'initial sync.

---

<a name="7"></a>
## 7. Pièges Mono/Unity récapitulés (checklist)

1. **Inlining** : patcher tôt les petites méthodes ; sinon patcher les appelants
   (`TargetMethods`). Un patch silencieusement inerte = symptôme n°1. (§2.8)
2. **Patch trop tôt** : `MissingMethodException` si la cible appelle de l'extern Unity
   avant la fin du boot Unity. Nos patches persistants s'appliquent au plus tôt à
   `[ModEntryOnInitializationLoad]`, et si ça jette, on retente à
   `RegisterOnGameLoadedCallback`. (§2.9)
3. **Génériques partagés** : ne patcher que des cibles non génériques ou des
   instanciations value-type. (§2.7)
4. **Constructeurs statiques** : impatchables en pratique. (§2.4)
5. **Dead code / `throw` nu** : `InvalidProgramException` sous Mono → transpiler. (§2.9)
6. **`base.Method()`** depuis un patch → Reverse Patch. (§2.9)
7. **Async/coroutines** : patcher le `MoveNext` (ou fin de coroutine), pas la façade.
8. **Unpatch** : toujours `UnpatchAll(harmony.Id)` — jamais sans Id (on tuerait les
   patches des autres mods). (§1.3/1.5)
9. **Logs Harmony** : `FileLog` écrit sur le Bureau — laisser `Harmony.DEBUG=false` en
   release ; en 2.4, `HARMONY_NO_LOG`/`HARMONY_LOG_FILE` existent pour le support.
10. **Une seule instance `Harmony` par assembly** (CE le commente, Nitrox le fait) ;
    Id en domaine inversé, stable entre versions du mod.
11. **Suppresseurs = main thread only.** (§6.1)
12. **Ne jamais jeter depuis un patch** : envelopper le corps des callbacks réseau
    (`try { … } catch (Exception e) { Log.Error(...) }`) — une exception de patch
    remonte dans le gameplay du jeu.

---

<a name="8"></a>
## 8. Plan Harmony pour CoopAmbitions

### 8.1 La lib à embarquer, précisément

- **Paquet** : NuGet **`Lib.Harmony` 2.4.2** (pardeike). PAS `Lib.Harmony.Thin`
  (celui-là exige les DLL MonoMod à côté), PAS HarmonyX (traîne
  `MonoMod.RuntimeDetour ≥ 25.3.4` et 4-6 DLL sœurs — §1.2).
- **Fichier** : `0Harmony.dll` du dossier **`lib/net472/`** du paquet (Unity 2022.3
  Mono = profil API .NET Framework 4.7.x ; net48 marcherait aussi, net472 est le
  match exact ; ~1,5 Mo, MonoMod fusionné/internalisé).
- **Installation** : bouton « Add Dep » du Mod Builder BA (→
  `Assets/Mods/CoopAmbitions/Dependencies/0Harmony.dll`, câblage asmdef
  `precompiledReferences` automatique, livraison dans
  `Output/CoopAmbitions/Dependencies/` — chemin détaillé dans
  `docs/research/sdk-officiel.md` §2.6).
- **Épinglage** : noter la version dans le manifest/README ; on ne monte de version
  qu'en testant l'interop avec les mods BA populaires du moment.
- **Id** : `com.coopambitions.patches`.

### 8.2 Squelette PatchManager + Suppressor

```csharp
// === Suppressor.cs — copie assumée du PacketSuppressor de Nitrox (§6.1) ===
public readonly struct Suppressor<T> : IDisposable where T : ICoopMessage
{
    private static int count;                      // MAIN THREAD ONLY
    public static bool IsSuppressed => count > 0;
    public static Suppressor<T> Suppress() { count++; return default; }
    public void Dispose() => count--;
}
public struct AllMessages : ICoopMessage { }       // suppression globale (initial sync)

// === CoopPatch.cs ===
public enum PatchCriticality { Critical, Degradable }

public abstract class CoopPatch
{
    public abstract MethodBase Target { get; }              // Reflect.* ou AccessTools, peut être null
    public virtual PatchCriticality Criticality => PatchCriticality.Critical;
    public virtual bool Persistent => false;                // true = appliqué au boot, jamais retiré

    public void Apply(Harmony harmony)
    {
        MethodBase target = Target;
        if (target == null) throw new InvalidOperationException("cible introuvable (MAJ du jeu ?)");
        var t = GetType();
        harmony.Patch(target,
            prefix:     Wrap(t, "Prefix"),
            postfix:    Wrap(t, "Postfix"),
            transpiler: Wrap(t, "Transpiler"),
            finalizer:  Wrap(t, "Finalizer"));
    }
    private static HarmonyMethod Wrap(Type t, string name)
    {
        MethodInfo m = t.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return m != null ? new HarmonyMethod(m) : null;
    }
}

// === PatchManager.cs ===
public sealed class PatchManager
{
    private readonly Harmony harmony = new("com.coopambitions.patches");
    private readonly List<CoopPatch> applied = new();
    public PatchReport Report { get; } = new();

    // Au [ModEntryOnInitializationLoad] : patches persistants uniquement.
    public void ApplyPersistent(IEnumerable<CoopPatch> all) => Apply(all.Where(p => p.Persistent));
    // À l'entrée en session coop : patches dynamiques.
    public void ApplySession(IEnumerable<CoopPatch> all)    => Apply(all.Where(p => !p.Persistent));

    private void Apply(IEnumerable<CoopPatch> patches)
    {
        foreach (CoopPatch patch in patches)
        {
            try
            {
                patch.Apply(harmony);
                applied.Add(patch);
                Report.Ok(patch);
            }
            catch (Exception e)
            {
                for (Exception inner = e; inner != null; inner = inner.InnerException) e = inner; // racine (pattern Nitrox)
                Report.Fail(patch, e);
            }
        }
        Log.Info(Report.Summary());   // "40 ok, 1 failed [...]" + Harmony.VersionInfo
        if (Report.HasCriticalFailure)
            CoopUi.BlockMultiplayer(Report);   // solo intact, coop refusé, message explicite
    }

    // Fin de session : on désarme les dynamiques (Unpatch rejoue la méthode sans nos patches).
    public void RestoreSession()
    {
        foreach (CoopPatch p in applied.Where(p => !p.Persistent).ToList())
        {
            if (p.Target is MethodBase t) harmony.Unpatch(t, HarmonyPatchType.All, harmony.Id);
            applied.Remove(p);
        }
    }

    // OnUnloadAsync du mod : tout retirer, TOUJOURS avec notre Id.
    public void UnpatchSelf() { harmony.UnpatchAll(harmony.Id); applied.Clear(); }
}
```

La découverte des patches se fait par réflexion sur notre assembly
(`typeof(CoopPatch).Assembly` → types concrets assignables), sans DI lourde — Autofac
chez Nitrox n'apporte rien à notre échelle. Chaque patch : un fichier dans
`Source/Patches/{Persistent,Session}/`, nommé `Type_Méthode_Patch.cs`.

Cycle de vie BA :

| Moment BA | Action |
|---|---|
| `[ModEntryOnInitializationLoad]` | `ApplyPersistent` (anti-inlining : le plus tôt possible, avec retry au GameLoaded si `MissingMethodException`) |
| host/join réussi | `ApplySession` + vérif `Report` dans le handshake |
| fin de session / retour menu | `RestoreSession` + reset des suppresseurs |
| `OnUnloadAsync` | `UnpatchSelf` |

### 8.3 Les 10 premières méthodes à patcher (ordre de mise en œuvre)

Cibles issues des rapports internals/SDK ; celles marquées 🔍 restent à confirmer au
dnSpy (noms exacts). Type : I = intention (Prefix, souvent `return false`), R =
résultat (Postfix), N = neutralisant (Prefix `return false` inconditionnel côté
invité).

| # | Cible | Type | Rôle coop |
|---|---|---|---|
| 1 | `GameManager.ChangeMoneySafe(montant, TransactionInfo, bool)` | I+R | LE point de mutation d'argent : invité → requête à l'hôte ; hôte → broadcast delta + solde (§6.3). Premier patch écrit, premier smoke test. |
| 2 | 🔍 tick du temps — la méthode du singleton `DayNightCycle` qui incrémente `Timestamp {Day, Hour, Minute}` | N (invité) | l'horloge invité ne tourne pas ; elle s'écrit depuis les `TimeSync` de l'hôte sous suppresseur |
| 3 | 🔍 pause / vitesse du temps (méthodes Pause/SetTimeScale du même singleton, + Update du menu pause) | I | en session, pause locale interdite → vote ; l'hôte applique, broadcast |
| 4 | `SaveGameManager.Save(SaveType, …)` | N (invité) | seul l'hôte persiste ; invité : `return false` + toast « l'hôte sauvegarde » ; hôte : Postfix pour signaler la save (et y injecter `modData` coop avant, via Prefix) |
| 5 | 🔍 achat/enregistrement d'un bâtiment (méthode qui crée `BuildingRegistration` — suivre « Used By » depuis `SaveGame.BuildingRegistrations`) | I+R | conflit majeur (bien unique) : réservation côté hôte obligatoire |
| 6 | 🔍 placement/retrait d'objets (`BigAmbitions.PlacementSystem` — la méthode de commit du placement, équivalent `Builder.TryPlace` de Nitrox) | I+R | co-aménagement des magasins : intention invité, résultat hôte, application distante sous suppresseur |
| 7 | `VehicleHelper.CreateAndSpawnVehicle(...)` (+ 🔍 la méthode d'achat qui l'appelle et débite) | R (hôte) / I (invité) | spawn de véhicules répliqué ; id via `UuidHelper.GenerateBase64Uuid` imposé par l'hôte |
| 8 | 🔍 embauche/licenciement (méthode créant/supprimant `EmployeeInstance`) | I+R | employés partagés d'une entreprise co-détenue — le différenciateur du mod |
| 9 | 🔍 ticks économiques planifiés (livraisons 2 h, imports minuit, frais bancaires — probablement des handlers sur `onNewHour`/`onNewDay`) | N (invité) | la simulation économique ne tourne que chez l'hôte ; l'invité reçoit les résultats |
| 10 | 🔍 spawn des clients IA (`AI.Customers`, `CustomerEntry.SpawnTime`) | N (invité) | clients simulés hôte-seulement ; réplication visuelle plus tard (phase PNJ) |

Chacun suit le canevas §6.3 : garde `CoopSession.Active` → garde suppresseur → branche
hôte/invité ; et chaque application de mutation distante rappelle la méthode du jeu
sous `using (Suppressor<X>.Suppress())`.

### 8.4 Definition of done du premier patch (n°1, argent)

1. `0Harmony.dll` net472 dans `Dependencies/`, build + install OK, `Harmony.VersionInfo`
   loggé au boot.
2. `GameManager_ChangeMoneySafe_Patch` appliqué sans erreur ; en solo, comportement
   strictement inchangé (garde `CoopSession.Active`).
3. Smoke test in-game : un achat quelconque fait apparaître notre ligne de log
   Prefix+Postfix (prouve : pas d'inlining, injections correctes).
4. À deux comptes Steam : l'achat d'un invité débite le portefeuille de l'hôte, le
   solde revient à l'invité, **aucun écho** (le suppresseur est le test).
5. `RestoreSession` puis re-achat solo : comportement vanilla — prouve l'unpatch.

---

## Sources

- **pardeike/Harmony 2.4.2** (MIT) — code : `/home/user/harmony` ; doc :
  `Documentation/articles/{basics,execution,patching-injections,patching-edgecases,priorities,patching-transpiler-matcher,utilities,reverse-patching}.md`
  (en ligne : https://harmony.pardeike.net) ; packaging fat :
  `Lib.Harmony/Lib.Harmony.csproj` + `ILRepack.targets` ; état partagé :
  `Harmony/Internal/HarmonySharedState.cs` ; unpatch :
  `Harmony/Public/Harmony.cs` (`UnpatchAll(string harmonyID = null)`).
- **Nitrox** — `/home/user/nitrox` : `NitroxPatcher/Patcher.cs`,
  `NitroxPatcher/Patches/NitroxPatch.cs`, `PacketSuppressorPatch.cs`,
  `NitroxClient/Communication/PacketSuppressor.cs`,
  `NitroxClient/GameLogic/Vehicles.cs`, patches cités dans
  `NitroxPatcher/Patches/Dynamic/` (`Bed_EnterInUseMode`, `Battery_charge_set`,
  `SpawnOnKill_OnKill`, `ToggleLights_SetLightsActive`, `BeaconLabel_SetLabel`),
  `Nitrox.Model/Helper/Reflect.cs`,
  `Nitrox.Test/Patcher/Patches/PatchesTranspilerTest.cs` ; HarmonyX 2.10.0 épinglé
  dans `Directory.Packages.props`.
- **Combat Extended** — `/home/user/combatextended/Source/CombatExtended/Harmony/` :
  `HarmonyBase.cs`, `Harmony_Thing.cs` (105 fichiers de patches).
- **SMAPI** — `/home/user/smapi` : `build/0Harmony.dll` (Harmony maison imposée),
  `src/SMAPI/SMAPI.csproj` (« tiered compilation breaks Harmony »),
  `Framework/ModLoading/Rewriters/StardewValley_1_5/HarmonyRewriter.cs` (détection
  Harmony 1.x par réécriture Cecil).
- **HarmonyX** — wiki : « Difference between Harmony and HarmonyX »,
  « Valid patch targets » (https://github.com/BepInEx/HarmonyX/wiki) ; NuGet 2.16.1
  (30/03/2026, dépendance `MonoMod.RuntimeDetour ≥ 25.3.4`)
  (https://www.nuget.org/packages/HarmonyX).
- **Big Ambitions SDK** — `/home/user/ba-official` (`ModValidator.cs`,
  `DependencyActions.cs`, `ModPackager.cs`) + `docs/research/sdk-officiel.md` §2.6 ;
  cibles de jeu : `docs/research/internals-du-jeu.md` §3.
