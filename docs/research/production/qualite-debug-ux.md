# Qualité, debug in-game et UX de mod — pratiques des meilleurs mods multijoueur

*Recherche du 2026-08-31, orientée « production confortable » pour CoopAmbitions (mod coop
Big Ambitions, Unity 2022.3 Mono, SDK officiel, transport Steam lobbies + relay).
Sources : clones locaux analysés (`/home/user/nitrox`, `/home/user/csm`, `/home/user/facepunch`,
`/home/user/jotunn`, `/home/user/tmodloader`, `/home/user/smapi`, `/home/user/mscmp`,
`/home/user/ba-official`) + recherche web. Complète [SYNTHESE.md](../SYNTHESE.md) et
[art-anterieur-multijoueur.md](../art-anterieur-multijoueur.md).*

**Le constat qui motive tout ce document** : l'équipe Nitrox elle-même dit que tester « la plus
petite chose » exige 2 lancements du jeu + 2 connexions au serveur + recréation de la situation,
soit **2-3 minutes par essai** (contre 10-20 s pour le studio qui a l'éditeur Unity)
([Nitrox 2024 Q&A](https://nitroxblog.rux.gg/2024/05/18/nitrox-2024-qa/)). Chaque outil de ce
document vise à raccourcir cette boucle ou à éviter d'avoir à la lancer.

---

## 1. Debug multijoueur en pratique : faire tourner 2 joueurs

### 1.1 Le problème Steam

Notre transport = lobbies Steam + SteamNetworkingSockets P2P identifiés par **SteamId**.
Conséquences :

- **Lancer 2 fois le jeu depuis le même compte** (en lançant `Big Ambitions.exe` directement,
  Steam ne bloque que le bouton « Jouer ») donne 2 instances avec **le même SteamId**. Le
  lobby ne peut pas s'auto-rejoindre et les deux pairs sont indistinguables → inutilisable
  pour tester le transport Steam. En revanche c'est **parfaitement utilisable avec un
  transport loopback/LAN** (voir §3) : c'est même la config de dev la plus rapide.
- Un seul client Steam connecté par session Windows → 2 comptes en même temps = il faut ruser.

### 1.2 Les solutions réelles, de la plus saine à la plus bricolée

| Solution | Coût | Fiabilité | Verdict |
|---|---|---|---|
| **1 PC + 1 laptop, 2 comptes** | 2e compte + accès au jeu | Conditions réseau réelles (relay Steam, NAT) | **La référence.** À faire avant toute release ; c'est le seul test qui couvre le vrai chemin réseau. |
| **2 comptes + Steam Family Sharing** | Gratuit si 2e machine dispo | ⚠️ Piège : depuis Steam Families (2024), **1 copie = 1 joueur à la fois**. Deux membres ne peuvent jouer au *même* jeu simultanément que si la famille possède **2 copies** ([Engadget](https://www.engadget.com/gaming/pc/steam-families-library-sharing-is-live-and-you-can-all-play-at-the-same-time-231044311.html), FAQ Valve) | Le partage familial ne suffit **pas** pour tester à 2 en même temps. |
| **Acheter une 2e copie** (compte secondaire, promo) | ~prix du jeu | Parfaite : 2 vrais comptes, invitations, overlay, relay | **Recommandé** : c'est l'outil de travail n°1 d'un mod MP ; amorti dès la première semaine de debug. Beaucoup d'équipes de mods MP font exactement ça. |
| **2 clients Steam sur 1 PC via Sandboxie-Plus** (ou 2 sessions Windows) | Gratuit (1 bac à sable) | Fonctionne pour beaucoup de jeux ; fragile (overlay, anti-triche, saves) ([guide Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=311943358)) | Plan B si une seule machine. Exige quand même 2 comptes possédant le jeu. Prévoir 2 dossiers de saves distincts. |
| **Goldberg emulator / gbe_fork** (remplace `steam_api64.dll`, émule lobbies/matchmaking en LAN) | Gratuit | Chaque instance reçoit un faux SteamId configurable (`user_steam_id.txt`) → 2 instances côte à côte ([Goldberg](https://gitlab.com/Mr_Goldberg/goldberg_emulator), [Universal Split Screen](https://universalsplitscreen.github.io/docs/goldberg/)) | ⚠️ **Pas de relay Steam** (le vrai SDR n'existe pas hors Steam) ; le vieux Goldberg n'implémente pas `ISteamNetworkingSockets`, le fork actif [gbe_fork](https://github.com/Detanup01/gbe_fork) en implémente une partie (lobbies protobuf en broadcast LAN, `ISteamNetworking` ancien style) — **à tester au cas par cas**. Légal/éthique : on possède le jeu, usage privé de dev, pas de contournement de vente — mais c'est hors ToS Steam et ça ne teste pas le vrai chemin réseau. À réserver au dépannage, jamais documenté côté joueurs. |
| **Spacewar (appid 480)** | — | Non pertinent pour nous | Réservé aux **jeux en développement** qui choisissent leur appid ; un mod tourne dans le process de Big Ambitions avec l'appid du jeu, on ne peut pas en changer. (Et le matchmaking sur 480 est restreint/partagé avec la terre entière — [discussion GodotSteam](https://github.com/GodotSteam/GodotSteam/discussions/881).) |

**Recommandation CoopAmbitions** : boucle quotidienne = 2 instances même compte + transport
loopback (§3) ; validation hebdo = PC + laptop avec 2 comptes (2e copie achetée) ; le vrai
relay Steam est validé à chaque jalon, pas à chaque itération.

### 1.3 Debugger attaché et itération

- **Attacher le debugger managé** : Big Ambitions est Mono → poser
  `UNITY_GIVE_CHANCE_TO_ATTACH_DEBUGGER=1` et « Attach Unity Debugger » depuis
  VS/Rider fonctionne comme pour tout jeu Mono. C'est documenté tel quel par le mod
  My Summer Car Multiplayer (`/home/user/mscmp/howtodebug.md`). Attacher au *client* pendant
  que l'*hôte* tourne librement est la config la moins perturbante (l'hôte timeout sinon —
  prévoir un timeout de connexion généreux en build DEBUG, cf. `SteamNetworkingUtils.Timeout`,
  `/home/user/facepunch/Facepunch.Steamworks/SteamNetworkingUtils.cs:181`).
- **Fenêtrer les 2 instances** : lancer avec `-screen-fullscreen 0 -screen-width 1280`
  (arguments standard Unity player) pour voir hôte et client côte à côte.
- **Skip du menu** : un raccourci de dev « héberger direct + charger la dernière save » et
  « rejoindre le dernier hôte » (ligne de commande ou touche au menu) économise 30 s par essai —
  c'est LE micro-investissement que Nitrox regrette de ne pas avoir eu plus tôt (leur launcher
  fait aujourd'hui ce travail).

### 1.4 Tester à N > 2 joueurs

- Nitrox ne teste pas à N via bots : ils testent serveur seul (le serveur .NET tourne sans le
  jeu, headless) + sessions communautaires. Nous n'avons pas de serveur headless (hôte-joueur),
  donc notre équivalent :
  - **Client fantôme** : un petit exécutable console qui référence nos assemblies réseau
    (protocole + transport), fait le handshake Hello/Welcome et rejoue un enregistrement de
    mouvements (§3.4). Ne nécessite ni Unity ni le jeu — uniquement possible si le protocole
    et le transport restent dans des classes sans dépendance UnityEngine (c'est déjà presque
    le cas : `CoopSession` ne dépend d'Unity que via le log).
  - Pour les tests Steam réels à 3+, ce sont des **playtests communautaires organisés**
    (Discord) — pattern Nitrox/Going Public. Les organiser tôt, avec la collecte de logs de §4.

---

## 2. Observabilité in-game

### 2.1 Le pattern « gestionnaire de debuggers » de Nitrox (à copier tel quel)

`NitroxClient/MonoBehaviours/NitroxDebugManager.cs` (clone local) :

- **F7** bascule le mode debug ; une fenêtre IMGUI maîtresse liste les debuggers disponibles
  avec cases à cocher + hotkeys (Ctrl+N réseau, etc.) ; Ctrl+C libère le curseur, Ctrl+R
  réinitialise les positions de fenêtres.
- Chaque outil hérite d'`AbstractDebugger` (fenêtre IMGUI à onglets, skin dérivée) :
  `NetworkDebugger`, `SceneDebugger`, `EntityDebugger`, `SoundDebugger`…
- Le tout est **enveloppé dans `#if DEBUG`** : zéro coût et zéro surface d'abus en release.
  (Nitrox garde aussi `NitroxConsole.DisableConsole = true` par défaut pour la console de
  triche du jeu.)

Leçon : ne pas construire « une grosse fenêtre debug », mais un **registre de petits
debuggers** à fenêtres indépendantes — ça grandit sans friction pendant toute la vie du mod.

### 2.2 L'overlay réseau : les métriques sont déjà calculées par Steam

Le `NetworkDebugger` de Nitrox (`NitroxClient/Debuggers/NetworkDebugger.cs`) montre le bon
niveau d'information : totaux envoyés/reçus (paquets + octets), **liste des 100 derniers
paquets** dépliables (contenu via `ToString()` du paquet), **compteur par type de message**
trié décroissant (fait apparaître immédiatement le type qui spamme), **filtre
blacklist/whitelist** pré-rempli avec les types bruyants (mouvement, sons…).

Pour la partie liaison, **tout est gratuit chez Steam** : `Connection.QuickStatus()`
(`/home/user/facepunch/Facepunch.Steamworks/Networking/Connection.cs:153`) remplit un
`ConnectionStatus` (`Networking/ConnectionStatus.cs`) avec :

- `Ping` (ms), `ConnectionQualityLocal/Remote` (0..1, taux de livraison vu de chaque côté),
- `In/OutPacketsPerSec`, `In/OutBytesPerSec`,
- `PendingReliable`, `PendingUnreliable`, `SentUnackedReliable` (octets en file — **l'alarme
  anti-congestion** : si `PendingReliable` grimpe, on sature le lien),

et `Connection.DetailedStatus()` renvoie le rapport texte complet de Valve (à logguer sur
demande). Notre overlay F8 = QuickStatus + compteurs par type de message + **diff d'horloge
hôte/client** (notre `TimeState` vs heure locale) + état de session (handshake, initial sync,
nb de joueurs) — aucune métrique à calculer nous-mêmes côté liaison.

### 2.3 Logging structuré multi-instances (le modèle Nitrox est excellent)

`Nitrox.Model/Logger/Log.cs` (Serilog) — à transposer presque tel quel :

- **Préfixe par joueur** : propriétés `PlayerName` et `SaveName` injectées par un *enricher*
  dans chaque event ; le template console rend `[HH:mm:ss.fff] [Bob][INF] message` et le
  fichier devient `game-{save}-{player}-.log`. Quand on lit côte à côte les logs de l'hôte
  et de l'invité, **la corrélation se fait par timestamp + préfixe** — millisecondes
  obligatoires.
- **Rotation** : fichier journalier roulant, `retainedFileCountLimit: 10`,
  `fileSizeLimitBytes: 200 MB`, `shared: true` (deux instances sur la même machine peuvent
  écrire sans se verrouiller mutuellement — notre cas en dev loopback !).
- **Niveaux** : `Debug()` est `[Conditional("DEBUG")]` → gratuit en release ;
  `WarnOnce`/`ErrorOnce` (cache par hash du message) évitent le spam des erreurs par frame —
  indispensable dans un `Update()` réseau.
- **Rédaction des données sensibles** : `InfoSensitive(...)` pousse un enricher qui remplace
  username/password/ip/hostname/path par des `***` dans le fichier — pertinent pour nous
  (SteamIds, noms de comptes) puisque ces logs seront envoyés publiquement (§4).
- **Double sortie jeu** : `Log.InGame(msg)` route vers un `InGameLogger` qui affiche à
  l'écran via l'API du jeu (`Nitrox.Model.Subnautica/Logger/SubnauticaInGameLogger.cs` utilise
  le toast natif de Subnautica). Chez nous : toast/notification Big Ambitions (à identifier
  dans dnSpy — le jeu a un système de notifications) sinon petit bandeau uGUI.

Concrètement pour CoopAmbitions : un wrapper `CoopLog` statique autour de `ModContext.Logger`
(+ fichier propre si le logger SDK ne fait pas de fichier par instance), avec
`[Host]`/`[Guest:Nom]` en préfixe, `Debug` conditionnel, `WarnOnce`, et un tag de **version du
mod + buildid du jeu** en tête de chaque fichier (première ligne à lire dans tout report).

### 2.4 Console de commandes in-game

Deux patterns observés :

- **Chat-console** (CSM) : le chat multijoueur parse les `/commandes` (`/sync` pour forcer un
  re-snapshot complet !) — `csm/Panels/ChatLogPanel.cs`. Le chat sert aussi de canal de
  notifications (« X s'est déconnecté : timeout »).
- **Fenêtre debug dédiée** (Nitrox) : commandes côté serveur + debuggers côté client.

Pour nous, la version minimale rentable : un champ texte dans l'overlay F8 avec 5 commandes :
`dump` (§2.5), `sync` (redemander un snapshot d'autorité), `lag <ms> <loss%>` (§3.2),
`time` (affiche horloges hôte/client/écart), `players`. Le chat viendra plus tard et
récupérera ces commandes.

### 2.5 Dumps d'état à la demande : diagnostiquer un desync

Aucun mod étudié n'a d'outil de diff automatique (CSM répare au `/sync`, Nitrox à la
reconnexion) — c'est une **occasion de faire mieux à peu de frais** :

1. Définir par domaine synchronisé un **`StateFingerprint`** sérialisable : argent (centimes),
   `Day/Hour/Minute`, nb de véhicules + hash de leurs ids, nb d'employés, hash des stocks par
   entreprise… La règle : uniquement des valeurs **déterministes et comparables**, pas de
   floats de position.
2. Commande `dump` : le client envoie un `StateDumpRequest`, l'hôte répond son fingerprint,
   le client écrit `dump-host.json` + `dump-client.json` + **le diff champ par champ dans le
   log et à l'écran**. Coût : une classe + 2 messages.
3. En build DEBUG, comparer automatiquement le fingerprint à chaque snapshot périodique et
   logguer `WarnOnce` par domaine divergent : les desyncs sont détectés **à la seconde où ils
   naissent**, pas quand le joueur les remarque (leçon CSM : une économie qui dérive en
   silence est indéboguable après coup).

---

## 3. Simulation et rejeu : tester le netcode sans lancer le jeu

### 3.1 Boucle en mémoire : extraire `ITransport`

Aujourd'hui `CoopSession` instancie `SteamTransport` en dur
(`CoopAmbitions/Scripts/Net/CoopSession.cs`). Extraire une interface :

```csharp
public interface ITransport : IDisposable {
    bool IsRunning { get; } bool IsHost { get; }
    event Action<ulong> PeerConnected, PeerDisconnected;
    event Action<ulong, byte[]> MessageReceived;
    void StartHost(); void Stop(); void Tick();
    void Send(ulong peer, byte[] data, bool reliable);
}
```

- `SteamTransport` l'implémente (rien d'autre ne change) ;
- **`LoopbackTransport`** : deux instances reliées par des `Queue<byte[]>` en mémoire, avec
  latence artificielle (délai d'échéance par paquet), perte (Random), et réordonnancement
  optionnel. ~100 lignes, zéro dépendance Unity/Steam.

Gains immédiats : (a) tests unitaires du handshake, de l'initial sync, du PacketSuppressor
et de tout processeur de message **en xUnit/MSTest pur**, en millisecondes ; (b) mode
« 2 instances même compte » sur une machine (§1.1) via LAN loopback si on branche plus tard
LiteNetLib.

C'est exactement la topologie de tests de Nitrox : leur machine à états de session est testée
sans le jeu (`Nitrox.Test/Client/Communication/MultiplayerSession/…`), le PacketSuppressor
aussi (`PacketSuppressorTest.cs`), et surtout **`PacketsSerializableTest.cs`** : réflexion sur
tous les types de `Packet`, génération de 2 instances aléatoires par type (faker), et
vérification **round-trip sérialisation → désérialisation → égalité profonde** (+ inégalité
avec la 2e instance pour prouver que la comparaison mord). À reproduire pour `NetMessage` :
ce test attrape chaque champ oublié dans `Write`/`Read` — la cause n°1 de bugs de protocole
binaire manuel.

Mentions honorables côté tests : Nitrox valide aussi ses **patches Harmony contre les DLL du
jeu** (`Nitrox.Test/Patcher/Patches/PatchesTranspilerTest.cs` : chaque transpiler doit
produire le delta d'instructions IL attendu sur la vraie méthode du jeu) → notre équivalent
low-cost : un test qui vérifie par réflexion que **chaque méthode ciblée par nos patches
existe encore** dans les DLL importées ; il casse dès qu'une MAJ du jeu renomme quelque chose,
avant même de lancer le jeu.

### 3.2 Latence/perte simulées sur le vrai transport

Deux étages, tous deux déjà écrits par d'autres :

- **Steam sockets** : `SteamNetworkingUtils` expose `FakeSendPacketLoss`, `FakeRecvPacketLoss`
  (0-100 %), `FakeSendPacketLag`, `FakeRecvPacketLag` (ms) —
  `/home/user/facepunch/Facepunch.Steamworks/SteamNetworkingUtils.cs:136-167` (enum complet
  avec réordonnancement `FakePacketReorder_*` dans `Generated/SteamEnums.cs:2406`). Ça
  s'applique au process entier, y compris en jeu réel → commande `lag 150 5` dans l'overlay
  F8 et on teste « l'ADSL australien » sans quitter son bureau. **Vérifier que la version de
  Facepunch embarquée par BA expose bien ces propriétés** (sinon passer par
  `SetConfigFloat(NetConfig.FakePacketLag_Send, …)` qui existe depuis longtemps).
- **LiteNetLib** (si transport LAN v2) : `NetManager.SimulateLatency` /
  `SimulatePacketLoss` + `SimulationMinLatency/MaxLatency/PacketLossChance`, actifs en build
  DEBUG ([doc LiteNetLib](https://revenantx.github.io/LiteNetLib/api/LiteNetLib.NetManager.html)).
- Et le `LoopbackTransport` a ses propres curseurs pour les tests unitaires.

Règle de production : **chaque feature de sync est validée une fois à 150 ms + 5 % de perte**
avant d'être considérée finie. C'est le test que ni CSM ni SuperMP ne faisaient (économies qui
dérivent, prêts non synchronisés) et que le simulateur rend gratuit.

### 3.3 Record/replay de sessions de paquets

Aucun des mods étudiés ne l'a ; c'est pourtant peu cher une fois `ITransport` extrait :

- **Record** : un décorateur `RecordingTransport(ITransport inner)` écrit
  `(timestampMs, direction, peerId, bytes)` dans un fichier (format binaire trivial,
  méta en tête : version protocole, buildid). Activable par commande console ; borné
  (buffer circulaire N Mo).
- **Replay** : un `ReplayTransport` rejoue le fichier dans `CoopSession` (en test unitaire,
  ou même in-game pour re-voir un bug d'avatar). Rejouer le log d'un desync rapporté par un
  joueur dans un test = le bug devient reproductible à l'infini.
- Le **NetworkDebugger** (§2.2) est la version « 100 derniers paquets, visuelle » du même
  besoin ; le record/replay en est la version persistante. Commencer par le debugger,
  ajouter le record quand le premier desync non-repro apparaît.

### 3.4 Harnais « 2 sessions en mémoire »

Le test d'intégration roi, permis par le loopback : instancier **2 `CoopSession` dans le même
process de test** (hôte + invité) reliées par `LoopbackTransport`, avec un faux
`IGameStateProvider` (interface à extraire pour les lectures `SaveGameManager.Current` /
position joueur), puis scripter : connexion → handshake → l'hôte change l'heure → assert que
l'invité converge en < N ticks, y compris avec 200 ms de latence simulée. C'est le test qui
remplace 2-3 minutes de double lancement du jeu par 50 ms de CI.

---

## 4. Gestion d'erreurs orientée utilisateur

Les échecs de connexion sont le grief n°1 de tous les mods MP (le fil Steam de Going Public
est plein de « no connected, pls leave and retry » — message d'erreur **non actionnable**,
contre-exemple parfait).

### 4.1 Erreurs de connexion : les patterns qui marchent

- **Nitrox — enum de refus + description par flag** :
  `Nitrox.Model/MultiplayerSession/MultiplayerSessionReservationState.cs` : le serveur répond
  au join par un enum `[Flags]` (`REJECTED | SERVER_PLAYER_CAPACITY_REACHED |
  AUTHENTICATION_FAILED | UNIQUE_PLAYER_NAME_CONSTRAINT_VIOLATED…`), chaque flag portant un
  `[Description("The server is currently at capacity. Please try again later.")]` ;
  `Describe()` concatène les descriptions des flags levés → le client affiche un texte
  actionnable sans switch dupliqué. **À copier** : notre `JoinRefusedReason` avec
  descriptions localisées.
- **CSM — la version dans le message** : le handler de connexion refuse avec
  `"Client and server have different CSM Mod versions. Client: 5.1, Server: 5.2."`
  (`csm/Commands/Handler/Internal/ConnectionRequestHandler.cs:59-66`) — versions du jeu ET du
  mod comparées séparément, en major.minor seulement (le patch ne casse pas la compat, regex
  `MatchVersionString`). Toujours **imprimer les deux valeurs** : l'utilisateur sait qui doit
  mettre à jour.
- **CSM — troubleshooting guidé** : `Client.ShowTroubleshooting` est levé sur échec →
  bouton « ? » sur le panneau de join qui ouvre un panneau `DisplayTroubleshooting(isHost,
  port, hasVpn)` (`csm/Panels/MessagePanel.cs:256`) : texte différencié hôte/client
  (port forwarding, firewall, VPN Hamachi détecté via processus !), et un **test de port
  intégré au menu « Manage Server »**. Grâce au relay Steam nous échappons au NAT, mais
  l'équivalent : auto-diagnostic **avant** d'afficher une erreur générique —
  `SteamClient.IsValid ?` → « Steam n'est pas lancé » ; `SteamNetworkingUtils.Status`
  (dispo relay, rempli par callback `SteamRelayNetworkStatus_t`,
  `SteamNetworkingUtils.cs:49`) → « le relais Steam est injoignable, vérifiez votre
  connexion/firewall » ; timeout de lobby → « votre ami est-il en jeu avec le mod chargé ? ».
- **Jötunn (Valheim) — compat de mods déclarative** : attribut
  `[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]`,
  vérification à la connexion, et **fenêtre in-game listant précisément les mods/versions en
  cause** des deux côtés (`JotunnLib/Utils/ModCompatibility/`,
  [doc](https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html)).
  La sémantique version : **breaking réseau ⇒ bump minor au minimum**, la comparaison se fait
  en major.minor — exactement notre handshake versionné déjà codé ; il nous manque juste
  l'écran de restitution propre.
- **tModLoader — l'écran d'erreur canonique** :
  `patches/tModLoader/Terraria/ModLoader/UI/UIErrorMessage.cs` : boîte de message scrollable +
  boutons **Continue/Retry**, **Open Logs** (ouvre le dossier de logs), **Open Web Help**
  (URL d'aide contextuelle à CE message d'erreur), Skip, « Exit & disable all mods ». Le trio
  *message actionnable + accès aux logs en 1 clic + lien d'aide ciblé* est le standard du
  genre.

### 4.2 Codes d'erreur documentés

Pattern combiné recommandé : chaque échec affiché porte un code court stable (`CA-E01
STEAM_OFFLINE`, `CA-E02 RELAY_UNAVAILABLE`, `CA-E03 LOBBY_NOT_FOUND`, `CA-E04
VERSION_MISMATCH host=x guest=y`, `CA-E05 JOIN_TIMEOUT`, `CA-E06 SAVE_TRANSFER_FAILED`…),
et le README/wiki a une ancre par code (le « Open Web Help » de tModLoader pointe alors sur
`…/wiki/erreurs#ca-e04`). Les reports Discord passent de « ça marche pas » à « CA-E04 » —
triage instantané.

### 4.3 Le bouton « report bug » qui empaquette les logs

- **Going Public le fait** : bouton dans la barre du haut qui empaquette automatiquement les
  logs (cf. [art-anterieur-multijoueur.md](../art-anterieur-multijoueur.md) §Going Public) —
  et c'est cité par leurs joueurs comme un vrai plus.
- **Nitrox** ne zippe pas en un clic (le launcher guide vers le dossier de logs ; un
  `TroubleshootService` vérifie au démarrage les problèmes d'environnement — droits fichiers,
  « ne lancez pas depuis un zip/OneDrive », `Nitrox.Launcher/Models/Services/TroubleshootService.cs`).
- **SMAPI (Stardew)** a la solution la plus aboutie : les joueurs uploadent leur log sur
  [smapi.io/log](https://smapi.io/log) qui le **parse et le met en forme** pour le support
  (code : `/home/user/smapi/src/SMAPI.Web/Framework/LogParsing/LogParser.cs`). Hors budget v1,
  mais ça montre pourquoi un **format de log régulier et auto-descriptif** paie.

Implémentation chez nous (S, ~1 jour) : commande/bouton « Signaler un problème » →
`System.IO.Compression.ZipFile` empaquette : nos logs (hôte **et** local — les fichiers
roulants du jour), `Player.log` d'Unity, un `report.json` (version mod, buildid jeu, OS,
locale, rôle hôte/invité, nb joueurs, uptime session, dernier code d'erreur, fingerprint
d'état §2.5) → dépose `CoopAmbitions-report-<date>.zip` sur le Bureau + ouvre l'explorateur +
copie le chemin dans le presse-papier, avec le lien Discord/GitHub Issues affiché. Penser à la
**rédaction** (§2.3) : pas de SteamId64 en clair dans le zip.

### 4.4 Compatibilité de version affichée dans le jeu

- Au menu principal : badge discret « CoopAmbitions v0.3.1 — jeu build 1234567 ✓ » ; si le
  buildid du jeu ≠ celui contre lequel les DLL ont été importées/testées (le SDK BA trace le
  buildid, cf. [sdk-officiel.md](../sdk-officiel.md)), le badge passe orange : « Le jeu a été
  mis à jour, le mod n'est pas encore validé pour cette version — risques d'erreurs. » C'est
  la réponse préventive au flot de reports qui suit *chaque* patch du jeu (leçon récurrente
  des dev blogs Nitrox).
- Version min/max supportée du protocole dans le handshake (déjà codé) + message CSM-style
  avec les deux versions et **qui doit agir** (« L'hôte doit mettre à jour le mod » vs
  « Vous devez… »).
- Option plus tard : un fichier JSON statique hébergé (GitHub Pages/raw) listant
  `buildid jeu ↔ version mod min`, consulté au lancement (avec timeout court et échec
  silencieux) → le badge sait avant nous que « la 0.3 casse avec le patch de mardi ».

---

## 5. UI de mod dans un jeu qui n'a pas prévu d'UI de mod

### 5.1 Les quatre approches

| Approche | Pour | Contre | Usage chez les mods étudiés |
|---|---|---|---|
| **IMGUI (`OnGUI`)** | 0 dépendance, 10 lignes, parfait pour du debug | Moche, coûteux en alloc/frame, pas de layout riche, saute avec le DPI | Nitrox : **tous** les debuggers (§2.1) — et rien d'autre. tModLoader/CSM : jamais pour l'UX joueur |
| **uGUI construit par code** | Contrôle total, s'insère dans le Canvas du jeu, thème du jeu réutilisable | Verbeux (chaque RectTransform à la main) | CSM (via ColossalUI, équivalent), Jötunn `GUIManager.Create*` (boutons/panneaux stylés Valheim générés par code) |
| **Prefab AssetBundle avec Canvas** | UI designée dans l'éditeur Unity, propre, maintenable | Pipeline de build (bundle par version Unity), pièges TMP/shaders | Jötunn (fenêtre de compat = prefab embarqué `Unity/Assets/Prefabs/CompatibilityWindow.prefab`), **la voie SDK BA** : `IModBigAmbitions.RelativeAssetBundlePaths` charge nos bundles nativement |
| **Framework tiers (UniverseLib)** | UI complète (panneaux, scroll, input) sans asset, gère Mono+IL2CPP, curseur/EventSystem | Dépendance de plus à embarquer, look générique | [UniverseLib](https://github.com/sinai-dev/UniverseLib) est le socle d'UnityExplorer ; pertinent si on veut un inspecteur riche, pas pour notre UI joueur |

### 5.2 Recommandation CoopAmbitions

- **Debug/overlay : IMGUI assumé** (pattern Nitrox, `#if DEBUG` sauf l'overlay F8 qui reste
  en release car utile aux joueurs pour les reports — version épurée).
- **UX joueur (panneau session, erreurs, vote de skip) : la voie SDK** — le projet Unity
  du SDK est déjà le nôtre, on fait un prefab Canvas (Screen Space Overlay, sorting order
  au-dessus du jeu, `CanvasScaler` en Scale With Screen Size 1920×1080) chargé via
  `RelativeAssetBundlePaths`. C'est ce que le SDK attend et ce que valide le ModValidator.
- **Options : `OptionsService` du SDK** — l'exemple officiel `Example-Options`
  (`/home/user/ba-official/Assets/Mods/Example-Options/Scripts/Logic/ExampleOptionsLogic.cs`)
  montre `new ModOptions().AddHeader(...).AddToggle(...).AddSlider(...).AddDropdown(...)` +
  `OptionsService.Register(modId, options)` : notre page de réglages (pseudo affiché, touche
  overlay, verbosité log) est **gratuite et native** — s'en servir avant de coder la moindre
  fenêtre custom.
- S'inspirer de Jötunn pour les réflexes d'intégration
  ([doc GUI](https://valheim-modding.github.io/Jotunn/tutorials/gui.html)) : re-créer/rattacher
  son UI **à chaque changement de scène** (menu ↔ ville), prévoir un `BlockInput` propre
  (couper les inputs du jeu quand notre fenêtre modale est ouverte, et **toujours** les
  rendre), et respecter le scaling DPI du jeu.

### 5.3 Pièges TextMeshPro (la cause n°1 de « texte carré rose »)

- Un prefab TMP dans un AssetBundle **embarque son `TMP_FontAsset`** ; si le bundle est
  construit sans les shaders TMP ou avec une autre version du package que celle du jeu, on a
  des glyphes roses/invisibles. Deux parades éprouvées :
  1. **Réutiliser la police du jeu au runtime** (pattern Jötunn : `GUIManager` récupère les
     `TMP_FontAsset` de Valheim par nom via le cache de prefabs,
     `JotunnLib/Managers/GUIManager.cs:391-403`) : au chargement de notre prefab, remplacer
     la police de chaque `TMP_Text` par
     `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()` filtré sur le nom de la police de BA →
     cohérence visuelle + localisation (glyphes accentués déjà dans l'atlas du jeu).
  2. Comme on construit dans **le projet SDK officiel** (même version Unity 2022.3.62f2,
     même pipeline), le risque de mismatch de version TMP est faible — mais tester le prefab
     dans le jeu dès le premier jour, pas à la fin.
- Ne jamais référencer `TMP Settings`/police par défaut du package dans le prefab (elle peut
  ne pas exister dans le player du jeu) : toujours une police explicite, remplacée au runtime.

---

## 6. Accessibilité de l'onboarding

Ce qu'on observe chez les mods qui « marchent tout seuls » :

- **Rejoindre = accepter une invitation Steam** (déjà notre design : lobby d'ami + overlay).
  Ajouter les deux compléments gratuits du même système :
  - `RichPresence` (« Joue à CoopAmbitions — 2/4 joueurs ») + **« Rejoindre la partie »**
    dans le menu ami Steam (lobby joinable), le canal de join le plus naturel qui soit ;
  - re-proposer automatiquement la reconnexion au dernier hôte après un crash (nos
    personnages invités persistent dans `modData` — pattern Going Public).
- **Zéro configuration** côté invité : pas de port, pas d'IP, pas de fichier à éditer. Toute
  option avancée vit dans `OptionsService` avec des défauts sains.
- **Doc « première partie »** (README + Workshop + wiki) : une page, **5 captures max**
  (activer le mod, F9 héberger, inviter via overlay, accepter l'invitation, écran de succès),
  une vidéo de 60 s (les fils Steam de Going Public montrent que les joueurs ne trouvent
  même pas l'invitation overlay — c'est la capture la plus importante).
- **FAQ des 10 échecs classiques**, chacune liée à un code d'erreur de §4.2 :
  1. Le mod n'apparaît pas en jeu (pas activé dans le gestionnaire de mods / mauvais dossier) ;
  2. Versions différentes hôte/invité (CA-E04) ;
  3. Le jeu vient d'être mis à jour, le mod pas encore (badge orange §4.4) ;
  4. Steam hors-ligne / mode invisible (CA-E01) ;
  5. L'invitation n'arrive pas (overlay désactivé → l'activer, ou passer par « Rejoindre la
     partie » clic-droit sur l'ami) ;
  6. Relais Steam injoignable / firewall d'entreprise (CA-E02) ;
  7. Join bloqué sur « synchronisation » (transfert de save long — afficher une **barre de
     progression**, jamais un écran figé : un joueur qui ne voit rien bouger quitte à 20 s) ;
  8. L'hôte a quitté/crashé pendant le join ;
  9. Autre mod incompatible (liste des mods dans le handshake, à la Jötunn) ;
  10. Saves : « vais-je perdre ma partie solo ? » (non — expliquer le modèle « la save de
      l'hôte est la vérité », le premier sujet d'angoisse des joueurs de coop-mods).
- **Un seul canal de support** mis en avant (Discord), avec le zip de report §4.3 comme
  premier réflexe demandé.

---

## Kit de confort pour CoopAmbitions

Priorisé. Effort : S ≤ 1 j, M = 2-4 j, L ≥ 1 sem. Moment : **maintenant** (avant/pendant le
MVP transport), **avant premier test à 2** (le jalon « F9 → invitation → se voir marcher »),
**avant release** (première version publique).

| # | Outil | Contenu | Effort | Moment |
|---|---|---|---|---|
| 1 | **`CoopLog` préfixé** | Wrapper logger : préfixe `[Host]/[Guest:nom]`, timestamps ms, `Debug` conditionnel, `WarnOnce/ErrorOnce`, fichier par instance avec rotation, en-tête version mod + buildid jeu (modèle : `Nitrox.Model/Logger/Log.cs`) | S | **Maintenant** — tout le reste écrit dedans |
| 2 | **`ITransport` + `LoopbackTransport`** | Interface extraite de `SteamTransport` ; loopback en mémoire avec latence/perte réglables ; permet 2 instances même compte et les tests sans jeu | S | **Maintenant** — plus jamais aussi bon marché qu'avant que le code grossisse |
| 3 | **Test round-trip des messages** | Pattern `PacketsSerializableTest` Nitrox : chaque `NetMessage` sérialisé→désérialisé→comparé (données aléatoires) | S | **Maintenant** — attrape les champs oubliés dans Write/Read à chaque nouveau message |
| 4 | **Overlay réseau F8** | IMGUI : état session, `QuickStatus()` par connexion (ping, qualité, pkt/s, PendingReliable), compteurs par type de message, diff horloge hôte/client (modèle : `NitroxDebugManager` + `NetworkDebugger`) | M | **Avant premier test à 2** — c'est l'instrument du test |
| 5 | **Messages d'erreur types + codes** | Enum `JoinRefusedReason` à descriptions (pattern Nitrox), versions des deux côtés dans le texte (pattern CSM), codes CA-Exx, auto-diagnostic Steam off / relay off / timeout | S | **Avant premier test à 2** — on va se les prendre nous-mêmes |
| 6 | **Simulateur de lien** | Commande `lag <ms> <loss%>` branchée sur `SteamNetworkingUtils.FakePacket*` + curseurs du loopback ; règle « toute feature validée à 150 ms / 5 % » | S | **Avant premier test à 2** |
| 7 | **Harnais 2-sessions en mémoire** | 2 `CoopSession` + loopback dans un test : handshake, initial sync, convergence d'horloge sous latence | M | Avant premier test à 2 (au plus tard pendant la phase 2 horloge) |
| 8 | **Fingerprint d'état + commande `dump`** | Snapshot comparable par domaine (argent, temps, véhicules, employés), échange hôte↔client, diff loggué/affiché ; auto-comparaison à chaque snapshot en DEBUG | M | **Phase 3** (dès la première sync d'état monde) — l'outil anti-desync |
| 9 | **Test « les cibles de patch existent »** | Réflexion sur les DLL importées : chaque méthode visée par un patch Harmony est présente (mini-`PatchesTranspilerTest`) | S | Dès le premier patch Harmony |
| 10 | **Console mini (dans F8)** | `dump`, `sync`, `lag`, `time`, `players` | S | Avant release (dev : au fil de l'eau) |
| 11 | **Bouton « Signaler un problème »** | Zip logs (2 côtés si dispo) + `report.json` (versions, OS, rôle, dernier code erreur, fingerprint) sur le Bureau, SteamIds expurgés, lien Discord (pattern Going Public) | S-M | **Avant release** — non négociable, c'est le grief n°1 du genre |
| 12 | **Badge de version au menu** | « v0.3.1 — jeu build X ✓/⚠ », orange si buildid non validé ; message clair « qui doit agir » en cas de mismatch au join | S | **Avant release** |
| 13 | **UI joueur en prefab (voie SDK)** | Panneau session/erreurs/vote via AssetBundle Canvas (`RelativeAssetBundlePaths`), police TMP du jeu réassignée au runtime, options via `OptionsService` | M | Avant release (le debug vit très bien en IMGUI d'ici là) |
| 14 | **Doc première partie + FAQ 10 échecs** | 1 page, 5 captures, vidéo 60 s, FAQ liée aux codes CA-Exx | M | **Avant release** |
| 15 | **Record/replay de paquets** | `RecordingTransport`/`ReplayTransport` (décorateurs d'`ITransport`), buffer borné, rejouable en test | M | Après release, au premier desync non reproductible |
| 16 | **Page de compat hébergée** | JSON `buildid ↔ version mod` sur GitHub Pages, consulté au lancement (échec silencieux) | S | Après release, dès la première MAJ du jeu qui casse |
| 17 | **Client fantôme N joueurs** | Console app réutilisant protocole+transport, rejoue des sessions enregistrées | L | Seulement si on vise > 2-3 joueurs sérieusement |

**Matériel côté équipe (pas du code)** : une **2e copie du jeu sur un compte secondaire**
(le family sharing ne permet pas de jouer au même jeu à 2 sur 1 copie) + le laptop = le banc
de test réel ; Goldberg/gbe_fork uniquement en dépannage privé, jamais documenté aux joueurs.

**Fil rouge** : les items 1-3 se font en 2-3 jours et changent immédiatement le quotidien ;
4-7 font que le premier test à deux **diagnostique** au lieu de constater ; 8-14 font que la
release génère des rapports exploitables au lieu de « ça marche pas ».

---

## Sources

- Clones locaux : Nitrox (`/home/user/nitrox` — `NitroxDebugManager.cs`,
  `Debuggers/NetworkDebugger.cs`, `Nitrox.Model/Logger/Log.cs`,
  `MultiplayerSessionReservationState.cs`, `Nitrox.Test/**`), CSM (`/home/user/csm` —
  `Networking/Client.cs`, `Panels/MessagePanel.cs`, `ConnectionRequestHandler.cs`),
  Facepunch.Steamworks (`/home/user/facepunch` — `SteamNetworkingUtils.cs`,
  `Networking/ConnectionStatus.cs`), Jötunn (`/home/user/jotunn` — `GUIManager.cs`,
  `Utils/ModCompatibility/`, tutoriels `gui.md`/`networkcompatibility.md`), tModLoader
  (`/home/user/tmodloader` — `UI/UIErrorMessage.cs`), SMAPI (`/home/user/smapi` —
  `SMAPI.Web/Framework/LogParsing/`), MSCMP (`/home/user/mscmp/howtodebug.md`),
  SDK BA (`/home/user/ba-official` — `Example-Options`).
- Web : [Nitrox 2024 Q&A](https://nitroxblog.rux.gg/2024/05/18/nitrox-2024-qa/) ·
  [GitHub Nitrox](https://github.com/subnauticanitrox/nitrox) ·
  [Goldberg emulator](https://gitlab.com/Mr_Goldberg/goldberg_emulator) ·
  [gbe_fork](https://github.com/Detanup01/gbe_fork) ·
  [Goldberg / Universal Split Screen](https://universalsplitscreen.github.io/docs/goldberg/) ·
  [Steam Families : 1 copie = 1 joueur simultané](https://www.engadget.com/gaming/pc/steam-families-library-sharing-is-live-and-you-can-all-play-at-the-same-time-231044311.html) ·
  [2 comptes Steam sur 1 PC (Sandboxie)](https://steamcommunity.com/sharedfiles/filedetails/?id=311943358) ·
  [Spacewar vs vrai AppID (GodotSteam)](https://github.com/GodotSteam/GodotSteam/discussions/881) ·
  [LiteNetLib NetManager (SimulateLatency/PacketLoss)](https://revenantx.github.io/LiteNetLib/api/LiteNetLib.NetManager.html) ·
  [UniverseLib](https://github.com/sinai-dev/UniverseLib) ·
  [UnityExplorer](https://github.com/sinai-dev/UnityExplorer) ·
  [Jötunn NetworkCompatibility](https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html) ·
  [Going Public (Workshop)](https://steamcommunity.com/sharedfiles/filedetails/?id=3765662670) ·
  [SMAPI log parser](https://smapi.io/log).
