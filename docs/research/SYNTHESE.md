# Synthèse de la recherche — tout ce qu'il faut pour coder CoopAmbitions

*Compilation croisée des 5 rapports du dossier `docs/research/` (2026-08-31) :
[SDK officiel](sdk-officiel.md) · [Facepunch.Steamworks](facepunch-steamworks.md) ·
[Internals du jeu](internals-du-jeu.md) · [Art antérieur multijoueur](art-anterieur-multijoueur.md) ·
[Mécaniques du jeu](mecaniques-du-jeu.md)*

---

## 1. Verdict global : le mod est faisable, et la voie est claire

- Le jeu est **Unity 2022.3.62f2, backend Mono** (passé d'IL2CPP à Mono vers l'EA 0.11 pour
  permettre le modding officiel). Harmony fonctionne nativement, dnSpy décompile tout.
- Le **SDK officiel** (MIT) charge des assemblies C# arbitraires, sans sandbox : réseau,
  patches Harmony, tout est techniquement permis. HarmonyX s'embarque via `Dependencies/`.
- Le jeu embarque **Facepunch.Steamworks** → lobbies + relay Steam sans dépendance ajoutée.
- **Preuve d'existence** : le mod « Going Public » (Melaus) fait déjà une ville partagée,
  une horloge commune et des avatars complets. Il est closed source, chaque joueur y garde
  SES entreprises — la **co-propriété d'une même entreprise** est notre différenciateur.
- La **1.0 est sortie le 28 août 2026** ; des updates et un DLC arrivent → chaque MAJ Steam
  invalide les DLL importées (le SDK trace le buildid). Isoler les accès au jeu par domaine
  et prévoir la maintenance comme un feature.

## 2. Contradictions entre rapports — tranchées

| Question | Rapport « mécaniques » (sources web) | Rapport « internals » (fondé sur le code) | Verdict |
|---|---|---|---|
| Backend | IL2CPP | Mono (le SDK importe depuis `Big Ambitions_Data/Managed`) | **Mono** — les vieilles docs IL2CPP datent d'avant l'EA 0.11 |
| Format des saves | JSON | binaire `.hsg` (OdinSerializer probable) | **`.hsg` binaire** — à confirmer dans dnSpy, mais peu importe : on ne touche pas aux fichiers, on manipule `SaveGameManager.Current` en mémoire |
| Going Public dispo ? | présent sur le Workshop | — | Des indices suggèrent un **retrait du Workshop** (guidelines Steam) — à vérifier à la main ; leçon : distribution multi-canal + open source |

## 3. L'API du jeu — ce qui est confirmé par du code qui compile

Points d'accès attestés (SDK officiel + ~15 mods communautaires de Dudeldups) :

- **Joueur local** : `GameManager.Instance.playerController` (namespace global) ou
  `PlayerHelper.PlayerController` (`Helpers`). *Déjà branché dans `LocalPlayerLocator`.*
- **Argent** : `GameManager.ChangeMoneySafe(montant, TransactionInfo, bool)` — LE point de
  mutation à patcher pour répliquer les transactions. Taxonomie complète dans l'enum
  `LegacyRef.Transaction`.
- **État de partie** : `SaveGameManager.Current` (`BigAmbitions.SaveSystem.Legacy`) —
  `Money`, `Day/Hour/Minute`, `VehicleInstances`, `EmployeeInstances`, `Transactions`,
  `Contacts`, `BuildingRegistrations`… = la liste de l'état à synchroniser. Et
  **`modData` (Dictionary<string,string> persistant par save)** : le canal officiel pour
  persister l'état coop (personnages des invités, portefeuille commun) dans la save de l'hôte.
  `MarkChange()` + `Save(SaveType.Default, null, null)` pour sauvegarder programmatiquement.
- **Temps** : struct `BigAmbitions.DayNightCycle.Timestamp {Day, Hour, Minute}` ;
  événements **`GlobalEvents.onNewDay` / `onNewHour`** (+ `onEnterBuilding`,
  `onVehicleVariablesChanged`, `RegisterOnGameLoadedCallback` — démarrer le réseau là,
  pas à l'init).
- **Véhicules** : `VehicleHelper.CreateAndSpawnVehicle`, `AllPlayerVehicles`,
  `TeleportVehicleToGround`, `VehicleInstance {id, fuel, vehicleColorName}`,
  `UuidHelper.GenerateBase64Uuid()` pour les ids.
- **Entrées de mod** : `[ModEntryOnInitializationLoad]`, `[ModEntryOnCityLoad]`,
  `[ModEntryOnMainMenuLoad]` — plusieurs classes d'entrée par assembly autorisées.
- **13 règles du ModValidator** (détaillées dans le rapport SDK) à respecter pour builder.

À vérifier en premier dans dnSpy (liste complète ordonnée dans le rapport internals) :
le type exact de `playerController`, le singleton du temps (qui incrémente `Timestamp`),
l'inventaire exhaustif de `SaveGame`, l'enum `LegacyRef.Transaction`,
`BigAmbitions.DebugMode.dll` (commandes toutes faites : set money/time/teleport),
et le comportement du streaming intérieur/extérieur (`CityManager.LoadIndoors`) quand
deux joueurs sont dans des bâtiments différents — **la grosse question de design restante**.

## 4. Architecture retenue (validée par l'art antérieur)

1. **Hôte-joueur autoritaire.** Ni serveur dédié (coût Nitrox : ré-implémenter le monde),
   ni double simulation (sanction CSM : desync en secondes). L'économie, les clients IA et
   les employés tournent uniquement chez l'hôte ; les invités envoient des intentions,
   reçoivent des résultats.
2. **Transport v1 : Steam lobbies + relay** (déjà codé et vérifié contre le source
   Facepunch, corrections appliquées). IP directe/LAN (LiteNetLib + UPnP) en v2 si demandé.
3. **Réplication d'événements typés + snapshots correctifs.** Un message par action de
   gameplay, et un snapshot d'autorité périodique par domaine (heure ~60 s, soldes, stocks)
   qui écrase les dérives — leçon des prêts non-sync de SuperMP et de l'économie buggée de CSM.
4. **Harmony façon Nitrox** : patches une-méthode-cible, prefix court-circuitant quand le
   comportement solo doit être remplacé, et **PacketSuppressor dès le premier jour**
   (flag « j'applique du distant » consulté par tous les hooks émetteurs — sinon boucle d'écho).
5. **Save : l'hôte est la seule vérité.** Sérialisation désactivée chez les invités ; leurs
   personnages vivent dans `modData` de la save hôte (reconnexion après crash, comme Going
   Public) ; join = transfert d'état + initial sync par processeurs ordonnés + file d'attente
   (un invité à la fois, réseau muet jusqu'à la fin — pattern Nitrox).
6. **Temps** : neutraliser pause/accélération locale par patch ; horloge maître dérivée du
   temps réel avec resync ~60 s ; **skip de nuit par vote unanime** (pattern
   SleepManager/Going Public/Stardew) ; si plusieurs vitesses : machine à états
   Request/Response/Reached de CSM avec temps cible commun. Attention aux **ticks planifiés**
   (livraisons 2 h, imports minuit, frais bancaires du lundi, IRS/60 j) à rejouer lors des skips.
7. **PNJ** : employés visibles par un visiteur = répliqués (positions grossières simulées
   par l'hôte — les restockers invisibles étaient LE grief de SuperMP) ; piétons/trafic =
   cosmétique local, jamais synchronisé. Ownership léger (transient/exclusive) uniquement
   pour ce qu'un invité manipule (véhicule conduit, diable poussé).
8. **Avatars** : 10-20 Hz unreliable, position+vélocité+rotation, position **relative au
   bâtiment/véhicule**, correction par vélocité + téléport au-delà d'un seuil, animations
   par événements discrets. (Base déjà codée ; passer de l'interpolation simple à la
   correction de vélocité en phase 1.)
9. **Versioning + hygiène** : handshake versionné avant tout état (déjà codé), comparaison
   major.minor, mod checker à message d'erreur explicite, open source, distribution
   Workshop **et** GitHub Releases, pas de télémétrie, bouton de report qui empaquette les logs.

## 5. Contraintes réseau chiffrées (source Facepunch vérifiée)

- `SendType.Reliable` ≤ 512 Ko/message (le transfert de save au join devra être découpé) ;
  `Unreliable` : rester < ~1 Ko (MTU).
- Pas de canaux dans cette API (les « lanes » n'existent qu'à partir de la 2.4.0 — la DLL
  du jeu peut être plus ancienne) → multiplexer par un octet de type (déjà le cas).
- `ConnectionInfo.Identity` peut être bugué (=0) sur Facepunch < 2.4.0 → identifier par le
  premier message (déjà corrigé dans `SteamTransport`).
- Toujours appeler `base.OnConnected`/`base.OnDisconnected` dans le SocketManager
  (poll group + libération du handle — déjà corrigé).
- Pomper `SteamClient.RunCallbacks()` en plus de `Receive()` (déjà fait, garde anti-réentrance).

## 6. Ce que la recherche a déjà changé dans le code

| Fichier | Changement | Source |
|---|---|---|
| `SteamTransport.cs` | `base.OnConnected`/`OnDisconnected`, mapping SteamId via OnMessage, `RunCallbacks`, init relay anticipée | rapport Facepunch |
| `LocalPlayerLocator.cs` | heuristiques remplacées par `GameManager.Instance.playerController` + `PlayerHelper.PlayerController` | rapport internals + code SDK |

## 7. Prochaines étapes concrètes (ordre recommandé)

1. **Compiler** : Unity 2022.3.62f2 + SDK + DLL importées ; corriger ce qui reste
   (`ModContext.Logger` etc. — API certaine seulement à la compilation).
2. Déplacer le démarrage réseau sur `GlobalEvents.RegisterOnGameLoadedCallback`
   (ou `[ModEntryOnCityLoad]`) plutôt qu'à l'init.
3. **MVP à deux comptes Steam** : F9 → invitation → se voir marcher. Valider le transport
   avant toute logique de jeu.
4. Session dnSpy guidée par la liste du rapport internals (type du playerController,
   singleton du temps, champs de SaveGame, DebugMode.dll).
5. Phase 2 : horloge (patch pause + TimeState + vote de skip), avatars améliorés
   (vélocité, animations, état véhicule).
6. Phase 3 : événements monde (argent via patch `ChangeMoneySafe`, immobilier, meubles),
   `modData` pour les personnages invités, transfert de save au join.
7. Embarquer HarmonyX via `Dependencies/` du SDK dès qu'on pose le premier patch,
   avec le PacketSuppressor.
