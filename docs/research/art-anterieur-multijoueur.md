# Art antérieur : mods multijoueur de jeux solo (Unity/Mono)

*Recherche pour le projet CoopAmbitions (mod coop pour Big Ambitions) — 2026-08-31.*

> **Note de méthode.** L'accès direct à `steamcommunity.com`, `api.steampowered.com` et `web.archive.org` est bloqué par le proxy d'egress de cet environnement. Les informations sur la page Workshop de « Going Public » ont donc été reconstituées à partir des index des moteurs de recherche (snippets de la page, des discussions et des commentaires) — elles sont fiables mais à re-vérifier à la main depuis une machine non filtrée. Le code de Nitrox, MSCMP, CSM et SuperMP a été analysé de première main (clones locaux, chemins cités ci-dessous).

---

## 1. « Going Public: Multiplayer for Big Ambitions » — le concurrent direct

- **Page Workshop** : <https://steamcommunity.com/sharedfiles/filedetails/?id=3765662670> (Steam Workshop id `3765662670`, jeu `1331550`)
- **Auteur** : *Melaus*. Mod communautaire non affilié à Hovgaard Games (mention explicite sur la page).
- **Popularité** : ~94 évaluations au moment de l'indexation.

### Ce qui est synchronisé (d'après la description Workshop indexée)

- **Un seul New York partagé, « one living city »** : l'hôte héberge la ville ; chaque joueur y fait tourner **ses propres entreprises** (argent, employés, immeubles séparés). Ce n'est **pas** une entreprise commune : les autres joueurs apparaissent comme **rivaux** sur le leaderboard, en concurrence sur les mêmes quartiers et les mêmes clients.
- **Horloge partagée** (« same synced city on a shared clock »).
- **Avatars complets** : on se voit marcher, conduire, pousser des diables (hand trucks), travailler aux caisses ; on peut faire ses courses dans les magasins des autres et **monter passager** dans leurs voitures.
- **Interactions sociales** : chat joueur-à-joueur via le téléphone business, **dons et prêts** entre joueurs via le « Business hub ».
- **Temps qui avance par consentement** : l'avance rapide de la nuit se fait **par vote** (« everyone voting to skip the night — sleep, rest, or work straight through it »). L'hôte dispose de **réglages de rythme** (vitesse de drain des besoins, vitesse de récupération du sommeil, vitesse d'évolution du moral).
- **Sauvegarde portée par l'hôte** : « The host's save carries every player's character » — on peut quitter en cours de session, se reconnecter après un crash et reprendre où on en était. Les clients n'ont pas besoin de leur propre save.

### Hébergement / connexion

- **Steam invites** (clic droit → *Join Game*), **IP directe** et **LAN**. Trois transports/chemins de connexion — donc très probablement Steam Datagram Relay/P2P + socket UDP classique.

### Limites et problèmes connus (commentaires/discussions indexés)

- Erreurs de connexion au join (« not connected, please leave and retry »).
- **Employés aux caisses pas toujours visibles côté hôte** ; animations non vues par certains joueurs (fix « en cours » d'après l'auteur).
- **Desync** reconnus par l'auteur (« desync can be a number of things that I'll have to narrow down »).
- Pas de gestion d'entreprise réellement commune (les joueurs « s'entraident » mais chaque société reste mono-propriétaire).

### Ingénierie / distribution

- **Pas de code source public trouvé** : aucune recherche GitHub (« Going Public », « Big Ambitions multiplayer ») ne retourne de repo. Les **changelogs sont publiés sur des GitHub Releases** (repo privé avec releases publiques, nom inconnu) ; support via **Discord** ; bouton de **report de bug intégré** dans le jeu (barre du haut, empaquette les logs automatiquement). **Licence inconnue** (closed source de fait).
- **Fait marquant** : selon les snippets Google les plus récents, la fiche Workshop a été **retirée pour violation des Steam Community & Content Guidelines** (« this item has been removed from the community… visible only to those who had previously interacted with it »). À confirmer, mais si c'est le cas, c'est une leçon stratégique majeure : *ne pas dépendre exclusivement du Workshop pour la distribution* (et vérifier ce que les guidelines de Hovgaard/Steam tolèrent — hooks réseau, télémétrie, DLL injectées…).

Sources : [page Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3765662670), [Workshop Big Ambitions](https://steamcommunity.com/app/1331550/workshop/), [discussion « Multiplayer or Co-op in the future? »](https://steamcommunity.com/app/1331550/discussions/0/612031852355541814/), [forum officiel](https://forum.bigambitionsgame.com/t/multiplayer/390).

### Contexte modding officiel

Big Ambitions a un **SDK de modding officiel** ([hovgaardgames/bigambitions](https://github.com/hovgaardgames/bigambitions), licence **MIT**, Unity **2022.3.62f2**, cloné dans `/home/user/hovgaardgames/bigambitions`) : mods = assemblies C# + asset bundles + manifeste, importation des DLL du jeu dans le projet Unity, publication Workshop intégrée. Il existe aussi une voie **MelonLoader** ([article](https://melonloader.net/modding-big-ambitions-with-melonloader/)) et des mods SDK communautaires ([Dudeldups/big-ambitions-mods](https://github.com/Dudeldups/big-ambitions-mods)). Un mod multi peut donc se livrer comme mod SDK « propre » (comme Going Public, distribué sur le Workshop) plutôt qu'en injecteur externe.

---

## 2. Nitrox (Subnautica) — l'architecture de référence

Repo : <https://github.com/SubnauticaNitrox/Nitrox> — **GPLv3** — clone local analysé : `/home/user/nitrox`. Docs : [wiki](https://github.com/SubnauticaNitrox/Nitrox/wiki), [dev blog](https://nitroxblog.rux.gg/), [site](https://nitrox.rux.gg/).

### 2.1 Découpage de la solution

| Projet | Rôle |
|---|---|
| `NitroxPatcher` | Injecté dans le jeu ; applique les **patches Harmony** (349 fichiers dans `NitroxPatcher/Patches/Dynamic/`, ~20 « Persistent » actifs dès le menu). |
| `NitroxClient` | Logique multi côté jeu : MonoBehaviours (avatars distants, broadcasters), processors de packets, InitialSync. |
| `Nitrox.Model` / `Nitrox.Model.Subnautica` | **Assembly partagé client/serveur** : packets, DTOs (`NitroxVector3`…), sérialisation, horloge. Séparation générique vs spécifique-jeu. |
| `Nitrox.Server.Subnautica` | **Serveur dédié headless .NET** (pas d'Unity) : `BackgroundService`s, persistance du monde, autorité. |
| `Nitrox.Launcher` | Launcher grand public (install, patch, lancement serveur). |

### 2.2 Réseau et packets

- Transport : **LiteNetLib** (UDP). Chaque packet déclare sa fiabilité : `Nitrox.Model/Networking/NitroxDeliveryMethod.cs` mappe `UNRELIABLE_SEQUENCED` / `RELIABLE_UNORDERED` / `RELIABLE_ORDERED` / `RELIABLE_ORDERED_LAST` sur les modes LiteNetLib, plus des **canaux UDP** séparés (`UdpChannelId`).
- ~**130+ classes de packets typés** (`Nitrox.Model.Subnautica/Packets/`) : un packet = un événement de gameplay (`BedEnter`, `ItemPosition`, `ChatMessage`, `TimeChange`…). Sérialisation **BinaryPack** avec enregistrement automatique des unions par réflexion (`Nitrox.Model/Packets/Packet.cs`).
- Côté client, un **processor par type de packet** (`NitroxClient/Communication/Packets/Processors/*Processor.cs`) ; côté serveur idem (`Nitrox.Server.Subnautica/Models/Packets/Processors/`). Dispatch par type = très lisible et extensible.

### 2.3 Interception des actions du jeu (Harmony)

- Un patch = une classe `XxxYyy_Patch : NitroxPatch, IDynamicPatch` avec `TARGET_METHOD` résolu par expression (`Reflect.Method((Bed t) => t.EnterInUseMode(...))`) — refactoring-safe.
- Patterns : **Prefix qui court-circuite** la méthode d'origine quand le comportement solo doit être remplacé (ex. `Bed_EnterInUseMode_Patch` empêche l'animation de sommeil et envoie `BedEnter` — on dort quand *tout le monde* dort), **Postfix qui broadcast** après l'action locale, **Transpilers** pour les cas fins.
- **Pattern anti-écho crucial** : `PacketSuppressorPatch<T>` (`NitroxPatcher/Patches/PacketSuppressorPatch.cs`). Quand un client *applique* un événement distant, il rejoue le code du jeu — lequel repasserait dans les patches et renverrait le même packet en boucle. Le suppressor pose un flag thread-scopé (`PacketSuppressor<T>.Suppress()`) pendant l'application, et les patches n'émettent pas si suppressed. **Indispensable pour tout mod par réplication d'événements.**

### 2.4 Autorité et « simulation ownership »

- Le serveur est **autoritaire sur l'état**, mais ne simule pas la physique/IA : il **délègue la simulation de chaque entité à un client** via un registre de **locks par entité** (`Nitrox.Server.Subnautica/Models/GameLogic/SimulationOwnership.cs`) : lock `TRANSIENT` (préemptable) ou `EXCLUSIVE` (ex. joueur qui pilote un véhicule). Résolution des conflits centralisée côté serveur (premier arrivé, exclusive > transient).
- `EntitySimulation.cs` réattribue l'ownership quand un joueur charge/décharge une cellule du monde ou se déconnecte : l'entité passe au client le plus proche qui la « voit ». Les clients envoient les positions des entités qu'ils simulent (`EntityTransformUpdates`).
- Leçon : ce système distribue le coût CPU de l'IA et évite de réimplémenter l'IA côté serveur, au prix d'une complexité de handover réelle.

### 2.5 Joueur distant : avatar et interpolation

- `RemotePlayer` (`NitroxClient/GameLogic/RemotePlayer.cs`) : GameObject reconstruit avec le modèle du joueur, vitals HUD, animations pilotées par packets (`AnimationChangeEvent`, `FootstepPacket`).
- Émission : `PlayerMovementBroadcaster` (MonoBehaviour, `Update()`) envoie position + **vélocité** + rotation corps + rotation visée ; positions **relatives au sous-marin** quand le joueur est dans un véhicule-conteneur (référentiel local — pertinent pour BA : position relative à l'intérieur d'un magasin/métro).
- Réception : pas de buffer d'états à la Source ; approche simple par **correction de vélocité** (`MovementHelper.GetCorrectedVelocity`) : vélocité cible = vélocité distante + (écart de position / temps de correction), **téléportation au-delà de 20 m** ou si NaN, easing iTween pour les objets non-physiques, amortissement 0.9× sur l'angulaire pour éviter l'oscillation.

### 2.6 Synchronisation initiale au join

- Handshake en plusieurs temps : `MultiplayerSessionPolicyRequest` → `MultiplayerSessionPolicy` (mot de passe requis ?, max connexions, **version Nitrox autorisée — comparée sur major.minor uniquement**, cf. `Nitrox.Model.Subnautica/Packets/MultiplayerSessionPolicy.cs`) → réservation de session → **file d'attente de join** (`JoinQueueInfo` : position + timeout) : *un seul client fait son initial sync à la fois*.
- Puis un **mega-packet `InitialPlayerSync`** (`Nitrox.Model.Subnautica/Packets/InitialPlayerSync.cs`) : équipement, PDA, story, position de spawn, stats, autres joueurs, entités racines, ownerships initiaux, mode de jeu, permissions, **TimeData**, préférences… Côté client, une série d'**`InitialSyncProcessor`s ordonnés** (`NitroxClient/GameLogic/InitialSync/`) consomme ce packet étape par étape en coroutines (horloge d'abord, puis joueur, puis monde, puis joueurs distants). Le broadcast de mouvement est coupé tant que `InitialSyncCompleted` est faux.

### 2.7 Temps partagé

- `TimeService` serveur (`Nitrox.Server.Subnautica/Models/GameLogic/TimeService.cs`) : le temps de jeu = **temps réel de simulation accumulé** (Stopwatch monotone) + constante de départ ; il s'arrête quand le serveur « hiberne » (aucun joueur). **Resync broadcasté toutes les 60 s.**
- Correction d'horloge : offsets **NTP** des deux côtés (`Nitrox.Model/Networking/NtpSyncer.cs`, 6 services NTP, timeout 5 s), et si l'un des deux est offline, **fallback par moyenne de RTT** sur plusieurs secondes (`ClockSyncInitialSyncProcessor` → `ClockSyncProcedure`). Le client dérive le temps de jeu du temps réel corrigé — pas de « tick sync » par frame.
- **Skip de temps par consensus** : `SleepManager` serveur — chaque `BedEnter`/`BedExit` met à jour un set de joueurs au lit, broadcast du statut « x/n joueurs dorment », et **quand 100 % dorment** : timer de 5 s (fondu), puis `timeService.SkipTime(396 s)` + `SleepComplete` à tous. Le client, lui, gèle localement le joueur au lit sans lancer le sommeil solo (patch prefix). C'est LE modèle éprouvé pour le sommeil/skip de nuit.
- Patches dédiés au temps : `DayNightCycle_deltaTime_Patch`, `FreezeTime_Set_Patch` (neutralise les pauses solo : en multi, **personne ne peut mettre le monde en pause**).

### 2.8 Persistance et infrastructure

- La **sauvegarde vit côté serveur** (`PersistedWorldData`, `AutoSaveService`, `SaveService`) ; les clients n'ont aucune save locale : le join EST un chargement de save par le réseau.
- `LanBroadcastService` : découverte LAN par broadcast UDP LiteNetLib (le client liste automatiquement les serveurs locaux). `PortForwardService` : ouverture **UPnP** automatique. Commandes console serveur, permissions par joueur.

### 2.9 Ce qui a été dur (d'après docs/blog/issues)

- **Spawning asynchrone** de Subnautica → refonte complète vers un « unified Entity model » ; l'attribution d'ids stables aux objets du monde (`NitroxId` sur tout) est un chantier permanent.
- Véhicules multi-occupants (Cyclops) → nécessité formalisée d'« un seul client simulant, sans ambiguïté » (d'où les locks).
- Le dev blog ([#6 « Code, time and patience »](https://nitroxblog.rux.gg/2023/09/20/dev-blog-6-code-time-and-patience/), [#7](https://nitroxblog.rux.gg/2024/02/18/dev-blog-7-priorities-and-you/)) insiste sur le coût d'entretien : chaque mise à jour du jeu casse des patches ; le tri des priorités de sync est vital.

---

## 3. Autres mods coop de jeux solo Unity

### 3.1 MSCMP — My Summer Car Multiplayer

Repos : [CurtisVL/MSCMP](https://github.com/CurtisVL/MSCMP) (continuation), [org MSCMP](https://github.com/MSCMP), [MSCMP-OLD](https://github.com/MSCMP/MSCMP-OLD) — **GPLv3** — clone local : `/home/user/mscmp`.

- **Réseau : Steam P2P pur** (`Steamworks.NET` : lobbies Steam, `P2PSessionRequest_t`, invitations `GameLobbyJoinRequested_t`) — cf. `src/MSCMPClient/Network/NetManager.cs`. Pas de serveur : **2 joueurs, hôte + invité**.
- **Messages réseau générés** : un projet `MSCMPMessages` génère le code de (dé)sérialisation avant compilation — équivalent artisanal d'un protobuf.
- **Autorité hôte** : `NetManager.IsHost` ; les items ne spawnent que chez l'hôte (avec des hacks avoués en commentaire : « This is a hack to workout beer bottles not spawning on the remote client »).
- **Ownership par objet** : `ObjectSyncComponent`/`ObjectSyncManager` — chaque objet synchronisé porte un id et un propriétaire ; messages `ObjectSyncMessage`, prise/rendu d'ownership au moment des interactions.
- **Join = `FullWorldSyncMessage`** : l'hôte sérialise tout l'état monde (portes, véhicules, pickupables, météo, heure) dans un gros message au moment du join. Simple et suffisant à 2 joueurs.
- **Spécificité** : MSC est un jeu **PlayMaker** ; le mod s'accroche aux FSM (`EventHook`, transitions globales injectées) plus qu'à Harmony — leçon : *s'accrocher au niveau « intention de gameplay » du moteur du jeu, quel qu'il soit*.
- **Renoncements** : trafic IA très partiellement synchronisé, beaucoup d'objets du monde ignorés ; le projet est mort/repris plusieurs fois (WreckMP en successeur communautaire).

### 3.2 CSM — Cities: Skylines Multiplayer (jeu de gestion !)

Repo : [CitiesSkylinesMultiplayer/CSM](https://github.com/CitiesSkylinesMultiplayer/CSM) — **MIT** — clone local : `/home/user/csm`. [FAQ](https://github.com/CitiesSkylinesMultiplayer/CSM/wiki/Frequently-Asked-Questions), [Supported Features](https://github.com/CitiesSkylinesMultiplayer/CSM/wiki/Supported-Features).

- **Modèle** : client-serveur où **l'hôte est un joueur** ; transport **LiteNetLib**, commandes **protobuf-net** (`[ProtoContract]`), **UPnP + NAT hole punching** intégrés (port UDP 4230). Pas de limite de joueurs codée (mais « plus de bugs au-delà de 2 »).
- **Réplication d'événements uniquement** (« commands ») : `src/basegame/Commands/Data/` couvre routes, bâtiments, zones, lignes de transport, arbres, météo, **économie (argent/taxes/budget — « still buggy »)**, demande RCI… Chaque commande a son `CommandHandler`. Un `TransactionHandler` regroupe les actions multi-étapes.
- **Renoncement fondateur** : **citoyens et véhicules ne sont PAS synchronisés** — chaque client fait tourner sa propre simulation de population, qui **diverge** (issues [#272](https://github.com/CitiesSkylinesMultiplayer/CSM/issues/272)/[#273](https://github.com/CitiesSkylinesMultiplayer/CSM/issues/273) : « desync after some seconds »). La commande `/sync` re-transfère l'état. Leçon capitale : **répliquer les actions sans synchroniser (ni rendre déterministe) la simulation sous-jacente ⇒ divergence garantie.** Ils l'assument en ne traitant les agents que comme du cosmétique local.
- **LE pattern time-scale** : `src/csm/Helpers/SpeedPauseHelper.cs` (563 lignes) — une **machine à états de négociation** pour tout changement de vitesse (1×/2×/4×) ou pause : le joueur qui change la vitesse envoie `SpeedPauseRequest` (avec `RequestId`), chaque client répond (`SpeedPauseResponse`), tous convergent vers un **temps cible commun** (`_waitTargetTime` : instant réel pour play, temps de jeu pour pause), puis `SpeedPauseReached` confirme. États `Playing/Paused/Waiting*` ; les changements pendant une négociation sont ignorés. C'est la réponse la plus aboutie de l'art antérieur au problème « qui a le droit de changer la vitesse du temps, et comment tout le monde s'aligne sans sauter ».
- **Join** : l'hôte envoie sa save (le monde entier) au client au moment de la connexion (« up to 60 seconds »).

### 3.3 SuperMP — Supermarket Simulator Multiplayer (le plus proche de Big Ambitions)

Repo (release/README public) : [SatyPardus/SuperMP-Public](https://github.com/SatyPardus/SuperMP-Public) — clone local : `/home/user/supermp` ; [page itch](https://satypardus.itch.io/supermarket-simulator-multiplayer-mod). Mod **BepInEx**, jusqu'à ~10 joueurs, hôte-autoritaire, **abandonné/discontinué** (le jeu a ensuite eu des concurrents coop natifs).

- **Save** : « For people who connect, the Save file system is completely disabled. For the host, the save file is modified as if he would play alone. » — même choix que Going Public : **la save de l'hôte est LA vérité**, sérialisation clients désactivée.
- **Known issues officiels** (README) — la liste des renoncements est éloquente pour notre genre :
  - « **Restockers are not fully synced and invisible on clients**, the boxes they carry just float magically around » — **les employés IA n'ont jamais été correctement synchronisés** ;
  - sons non synchronisés ; pas d'animations correctes des joueurs ;
  - « **Bank loans are not synced** and can be abused for infinite money » — l'économie partiellement synchronisée = exploits ;
  - factures auto-payées « maybe » synchronisées ; écrans des caisses incorrects quand un caissier IA scanne ;
  - **mod checker** : hôte et clients doivent avoir exactement les mêmes mods, sinon échec silencieux du join.
- Autre point notable : télémétrie intégrée non désactivable (SteamID + pseudo) → réaction communautaire négative. À éviter.

### 3.4 Stardew Valley « Makeshift Multiplayer » (avant le multi officiel)

Repo : [spacechase0/StardewValleyMP](https://github.com/spacechase0/StardewValleyMP) ([Nexus](https://www.nexusmods.com/stardewvalley/mods/501), abandonné après le multi officiel 1.3).

- Hôte + clients par IP (Hamachi/port forwarding conseillés), pop-up host/client au lancement.
- **Inventaire, argent et compétences partagés** (sauf la maison de chacun) — modèle « une seule ferme commune ».
- **La journée n'avance que quand tout le monde dort** — même pattern de consensus que Nitrox/Going Public.
- Leçon : un mod « makeshift » assumé (bugs ignorés) peut fédérer une énorme communauté en attendant mieux ; il a été rendu obsolète le jour où le studio a livré l'officiel.

### 3.5 Raft / RaftMMO

- Raft est coop natif depuis très tôt ; l'art antérieur intéressant est **[RaftMMO](https://github.com/maxvollmer/RaftMMO)** (Max Vollmer, [RaftModding](https://www.raftmodding.com/mods/raftmmo)) : plutôt que de synchroniser tout le monde, il fait **se rencontrer des sessions distinctes** en haute mer (bouée de rendez-vous, échange d'items, déconnexion en s'éloignant). Modèle « bulle de rencontre » : on ne synchronise qu'un **sous-ensemble d'état borné** (le radeau, les joueurs proches) pendant un temps borné. Idée réutilisable si on voulait un « mode visite » léger avant un vrai coop.

### 3.6 Green Hell, House Flipper, Townseek

- **Green Hell** : pas de mod coop antérieur notable — le **coop officiel (4 joueurs) est arrivé en V1.5** ([wiki](https://greenhell.fandom.com/wiki/Multiplayer)). Rien à réutiliser côté mod.
- **House Flipper 1** : **aucun mod multijoueur n'a jamais existé** ([gamepressure](https://www.gamepressure.com/newsroom/is-there-multiplayer-in-house-flipper/z04e70)) ; le studio a répondu par un **[DLC Co-op officiel de House Flipper 2](https://store.steampowered.com/app/3363370/House_Flipper_2__Coop_DLC/)**. Signal marché : dans les jeux de gestion « cozy », la demande coop est si forte que les studios finissent par la servir — un mod doit viser la fenêtre avant l'officiel.
- **Townseek** : jeu d'exploration/commerce **solo** ([Whales And Games](https://whalesandgames.itch.io/townseek)) — aucun mod MP trouvé. En revanche le site [Unmoddable](https://unmoddable.com/) (« Multiplayer game mods for Singleplayer games », ex. [LittleMultiplayer pour Townscaper](https://unmoddable.com/mods/townscaper-littlemultiplayer/)) recense ce micro-genre : la approche générique est toujours « avatars fantômes + événements ».

### 3.7 Derail Valley Multiplayer (bonus simulateur)

[Insprill/dv-multiplayer](https://github.com/Insprill/dv-multiplayer) (et forks [AMacro](https://github.com/AMacro/dv-multiplayer), version [Nexus](https://www.nexusmods.com/derailvalley/mods/1070)) : hôte-joueur, port UDP+TCP 4296 à ouvrir, LiteNetLib. Confirme le standard de facto des mods MP Unity récents : **hôte-joueur + LiteNetLib + patches Harmony**.

---

## 4. Synthèse des patterns

### 4.1 Modèle d'autorité pour un jeu de gestion

| Modèle | Exemples | Verdict pour un jeu de gestion |
|---|---|---|
| **Serveur dédié autoritaire** (réimplémente la logique monde en headless) | Nitrox | Robuste (monde persiste sans joueurs, anti-cheat), mais coût énorme : il faut ré-écrire l'économie/l'IA hors Unity et la maintenir à chaque patch du jeu. Justifié pour un survival à monde ouvert, pas pour un premier mod de gestion. |
| **Hôte-joueur autoritaire** | Going Public, MSCMP, SuperMP, CSM (hôte = joueur), Derail Valley MP | Le jeu de l'hôte EST le serveur : l'économie, les employés, les clients IA tournent une seule fois, chez l'hôte. Les clients envoient des *intentions* et reçoivent des *résultats*. C'est le standard de facto des mods coop Unity. |
| **Lockstep déterministe** | (aucun mod étudié n'y est parvenu) | Exige une simulation déterministe (float, ordre d'update, RNG) qu'on ne contrôle pas dans le jeu d'autrui. CSM montre la sanction : simulations parallèles ⇒ desync en secondes. À exclure. |

### 4.2 Réplication d'événements vs snapshots d'état

- Tous les mods étudiés font de la **réplication d'événements typés** (packets Nitrox, commands protobuf CSM, messages générés MSCMP) pour les actions discrètes — c'est le bon grain pour un jeu de gestion (acheter, poser un meuble, embaucher, fixer un prix).
- Les événements seuls dérivent (CSM economy « still buggy », loans SuperMP). Les correctifs observés : **resync périodique** (Nitrox : `TimeChange` toutes les 60 s ; CSM : `/sync` manuel = re-snapshot complet ; mod BA : bouton report/diagnostic). Le pattern gagnant est **hybride** : événements pour la réactivité + **snapshots d'autorité périodiques ou par domaine** (soldes bancaires, stocks, heure) qui écrasent l'état client.
- Anti-boucle d'écho obligatoire : **PacketSuppressor** (Nitrox) — flag « je suis en train d'appliquer du distant » consulté par tous les hooks émetteurs.

### 4.3 Transfert de sauvegarde au join

Consensus total de l'art antérieur :
1. **La save de l'hôte est la seule vérité** ; la sérialisation locale des clients est **désactivée** (SuperMP, Going Public).
2. Le join est un **chargement de save via le réseau** : `FullWorldSyncMessage` (MSCMP), transfert de la ville (CSM), `InitialPlayerSync` + processors ordonnés (Nitrox).
3. La save de l'hôte **contient les personnages de tous les joueurs** (Going Public) → reconnexion après crash sans perte.
4. **File d'attente de join** (Nitrox `JoinQueueInfo`) : un seul initial sync à la fois, broadcast de mouvement coupé tant que la sync n'est pas finie.

### 4.4 Le temps qui s'accélère/se met en pause — LE problème du jeu de gestion

Solutions observées, par ordre de sophistication :
1. **Interdire la pause solo** : Nitrox patche `FreezeTime`/`DayNightCycle` pour neutraliser toute pause locale ; le temps ne peut être que celui du serveur.
2. **Horloge maître + dérivation locale** : le serveur tient un temps de jeu monotone fonction du temps réel (Nitrox `TimeService`), le client calcule `tempsJeu = f(tempsRéelCorrigé)` avec correction d'offset (NTP double-face + fallback moyenne de RTT) et **resync périodique** (60 s). Personne ne « tick » le temps sur le réseau à chaque frame.
3. **Skip de temps par consensus unanime** : Nitrox `SleepManager` (x/n joueurs au lit, skip quand 100 %), Stardew Makeshift (la journée avance quand tous dorment), **Going Public (vote pour passer la nuit : dormir, se reposer, ou travailler pendant la nuit des autres)**. Le serveur exécute le skip et broadcast le nouveau temps — jamais les clients.
4. **Changement de vitesse négocié** : CSM `SpeedPauseHelper` — machine à états Request/Response/Reached avec **temps cible commun** pour que tous les clients basculent de vitesse au même instant de jeu, et ignorance des requêtes concurrentes pendant une négociation. À reprendre tel quel si Big Ambitions expose plusieurs vitesses.
5. Bonus Going Public : **réglages de rythme côté hôte** (drain des besoins, sommeil, moral) — reconnaître que le tuning solo ne survit pas au multi et l'exposer en options serveur.

### 4.5 Interpolation/extrapolation des avatars

- Émission ~10-20 Hz en **unreliable sequenced** : position + **vélocité** + rotations (corps/visée) ; position **relative au conteneur** (véhicule, bâtiment) quand embarqué (Nitrox).
- Réception : correction de vélocité (rattraper l'écart en `correctionTime`), **téléport au-delà d'un seuil** (20 m chez Nitrox) et sur NaN, easing (iTween) pour le non-physique, sous-correction volontaire (0.9×) de l'angulaire pour ne pas osciller.
- Animations : événements discrets (`AnimationChangeEvent`, footsteps) plutôt que sync de paramètres d'Animator à haute fréquence.
- Renoncements assumés dans le genre : SuperMP a vécu sans animations correctes ; mais l'attrait n°1 de Going Public est justement « se voir travailler dans les magasins des autres » → pour CoopAmbitions, l'avatar (marche/conduite/manutention) est un feature de premier plan, pas du polish.

### 4.6 Versioning de protocole et hygiène de session

- **Handshake versionné avant tout état** : Nitrox envoie la version autorisée dans `MultiplayerSessionPolicy` et compare **major.minor seulement** (patch releases compatibles).
- **Mod checker** (SuperMP) : vérifier le set de mods/DLC des deux côtés — mais avec un **message d'erreur explicite** (SuperMP échouait silencieusement, grief n°1).
- Sérialisation à unions enregistrées (BinaryPack chez Nitrox, protobuf field-numbers chez CSM) : les **ids de champs/types stables** rendent les évolutions de protocole tolérables.
- Connexion : proposer **les trois chemins** comme Going Public — invites Steam (P2P/SDR, zéro config), IP directe, LAN (broadcast UDP à la Nitrox) ; UPnP automatique (CSM, Nitrox) pour l'IP directe.

---

## 5. Décisions recommandées pour CoopAmbitions

1. **Hôte-joueur autoritaire, pas de serveur dédié, pas de lockstep.** L'économie, les clients IA et les employés de Big Ambitions tournent uniquement chez l'hôte ; les clients envoient des intentions et appliquent des résultats. Justification : c'est le modèle de Going Public, MSCMP, SuperMP et Derail Valley ; Nitrox montre le coût d'un serveur headless (ré-implémentation du monde), CSM montre la sanction du double-simulation (desync en secondes). Un serveur dédié pourra être extrait plus tard si le code est structuré client/« autorité » dès le départ (assembly partagé de packets à la `Nitrox.Model`).
2. **Transport : LiteNetLib pour IP directe + LAN, Facepunch.Steamworks (Steam Sockets/lobbies) pour les invites Steam.** Trois chemins de connexion comme Going Public ; découverte LAN par broadcast UDP et UPnP automatique repris de Nitrox/CSM. (Le clone de Facepunch.Steamworks est déjà dans `/home/user/facepunch`.)
3. **Réplication d'événements typés + snapshots correctifs par domaine.** Un packet C# par action de gameplay (achat, pose de meuble, embauche, prix, livraison), dispatch par processor à la Nitrox ; et un **snapshot d'autorité périodique** (heure, soldes, stocks, état des entreprises) qui écrase silencieusement les dérives — leçon des loans SuperMP et de l'économie « still buggy » de CSM. Sérialisation avec ids stables (MemoryPack/protobuf-net).
4. **Harmony avec le pattern Nitrox complet** : classes de patch une-méthode-cible avec `Reflect.Method`, prefixes qui court-circuitent le comportement solo quand il doit être remplacé, et surtout **PacketSuppressor** dès le premier jour pour éviter les boucles d'écho.
5. **Save : l'hôte est la seule vérité.** Sérialisation désactivée chez les clients ; la save de l'hôte porte les personnages/entreprises de tous les joueurs (reconnexion après crash, comme Going Public) ; le join = transfert de l'état complet + **initial sync par processeurs ordonnés** + **file d'attente de join** (un client à la fois, réseau muet tant que la sync n'est pas terminée).
6. **Temps : horloge maître chez l'hôte + consensus pour les skips.** (a) Neutraliser toute pause/accélération locale par patch (à la `FreezeTime_Set_Patch`) ; (b) temps de jeu dérivé du temps réel avec offset corrigé et **resync ~60 s** ; (c) **skip de nuit par vote unanime** géré par l'hôte (pattern SleepManager Nitrox = pattern Going Public = pattern Stardew) avec l'option « travailler pendant la nuit des autres » ; (d) si plusieurs vitesses de temps doivent exister, reprendre la **machine à états Request/Response/Reached de CSM** avec temps cible commun ; (e) exposer des réglages de rythme (besoins/sommeil) côté hôte.
7. **Synchroniser les employés/PNJ « qui comptent », renoncer aux foules.** Leçon SuperMP (restockers invisibles = grief majeur) vs CSM (citoyens non synchro = acceptable car cosmétique) : chez Big Ambitions, les **employés d'un magasin visibles par un visiteur** doivent être répliqués (positions grossières + états d'action suffisent, simulés par l'hôte) ; les piétons/trafic de rue restent locaux et cosmétiques. Prévoir un **ownership par entité** simple (locks transient/exclusive à la Nitrox) uniquement pour ce que les clients manipulent physiquement (véhicule conduit, diable poussé).
8. **Avatars : 10-20 Hz unreliable-sequenced, pos+vel+rotations, position relative au bâtiment/véhicule, correction par vélocité + téléport au-delà d'un seuil, animations par événements.** Copier `MovementHelper`/`PlayerMovementBroadcaster` de Nitrox ; l'avatar visible (marcher, conduire, tenir la caisse) est le cœur de la proposition de valeur — priorité haute.
9. **Versioning dès la v0.1** : handshake « policy » avant tout état avec version de protocole comparée sur major.minor, refus explicite en cas de mismatch, et **mod checker à message d'erreur clair** (l'échec silencieux de SuperMP est l'anti-pattern).
10. **Open source (GPLv3 ou MIT) et distribution multi-canal.** Nitrox (GPLv3), MSCMP (GPLv3), CSM (MIT) ont survécu à leurs auteurs grâce aux forks ; Going Public est closed source et sa fiche Workshop semble avoir été **retirée** — dépendre d'un seul canal fermé est fragile. Publier sur le Workshop via le SDK officiel MIT de Hovgaard **et** en GitHub Releases ; pas de télémétrie non-désactivable (leçon SuperMP). Vérifier explicitement les guidelines Workshop/Hovgaard sur les mods réseau avant le lancement.
11. **Prévoir la maintenance comme un feature** : bouton de report de bug intégré qui empaquette les logs (Going Public), Discord, et une architecture de patches isolée par domaine pour encaisser chaque mise à jour du jeu (leçon récurrente des dev blogs Nitrox).

---

### Annexe : clones locaux analysés

| Projet | Chemin local | Licence |
|---|---|---|
| Nitrox | `/home/user/nitrox` | GPLv3 |
| MSCMP (CurtisVL) | `/home/user/mscmp` | GPLv3 |
| CSM | `/home/user/csm` | MIT |
| SuperMP-Public (README/issues) | `/home/user/supermp` | n/a (binaire) |
| SDK officiel Big Ambitions | `/home/user/hovgaardgames/bigambitions` | MIT |
| Facepunch.Steamworks | `/home/user/facepunch` | MIT |
