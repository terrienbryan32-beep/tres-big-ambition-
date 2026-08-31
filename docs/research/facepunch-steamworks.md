# Vérification de SteamTransport.cs contre Facepunch.Steamworks (source réel)

Source vérifiée : clone officiel `github.com/Facepunch/Facepunch.Steamworks`
(master `b56e56a`, 2026-08-20), comparé au tag **2.3.2** (2020-02-28, ≈ NuGet 2.3.3
publié le même jour) et au tag **2.4.0** (2025-01-15). Fichiers de référence :
`SteamMatchmaking.cs`, `SteamNetworkingSockets.cs`, `SteamNetworkingUtils.cs`,
`SteamClient.cs`, `SteamFriends.cs`, `Networking/{SocketManager,ConnectionManager,Connection,ConnectionInfo,NetIdentity,NetMsg}.cs`,
`Structs/{Lobby,SteamId,Friend}.cs`, `Classes/Dispatch.cs`.

**Verdict global : toutes les signatures utilisées dans `SteamTransport.cs`
existent et sont exactes** (le fichier compile tel quel contre la vraie API).
Les problèmes sont **comportementaux** (appels `base.` manquants) et **de
version** (bug `ConnectionInfo.Identity` dans les DLL ≤ 2.3.3). Détail ci-dessous.

---

## Corrections requises

### C1 — CRITIQUE : `HostSocket.OnConnected` doit appeler `base.OnConnected`

`SocketManager.OnConnected` (identique en 2.3.2 et master) fait :

```csharp
public virtual void OnConnected( Connection connection, ConnectionInfo info )
{
    SteamNetworkingSockets.Internal.SetConnectionPollGroup( connection, pollGroup );
    Interface?.OnConnected( connection, info );
}
```

C'est **lui qui rattache la connexion au poll group**. `SocketManager.Receive()`
lit uniquement via `ReceiveMessagesOnPollGroup(pollGroup, …)`. L'override actuel
n'appelle pas `base` → **l'hôte ne recevra JAMAIS aucun message** (le `Pump()`
tournera à vide). Correction :

```csharp
public override void OnConnected(Connection connection, ConnectionInfo info)
{
    base.OnConnected(connection, info);   // ← indispensable (poll group)
    // ... le reste (MapConnection / RaisePeerConnected)
}
```

### C2 — IMPORTANT : `HostSocket.OnDisconnected` doit appeler `base.OnDisconnected`

Le `base` ferme le handle natif (`connection.Close()` quand `Interface == null`) ;
en 2.3.2 il retire aussi la connexion des listes `Connecting`/`Connected` et la
détache du poll group. Sans lui : fuite de handles natifs et, en 2.3.2, la
connexion morte **reste dans `Connected`** → `SendToAll` enverra sur des
connexions fermées. Correction : lire `SteamIdFor(connection.Id)` d'abord, puis
`base.OnDisconnected(connection, info);` (et retirer l'entrée de
`_connIdToSteamId` au passage).

### C3 — CRITIQUE si la DLL du jeu est ≤ 2.3.3 : `info.Identity` est buggé

En 2.3.2/2.3.3 le getter est littéralement :

```csharp
public NetIdentity Identity => address;   // BUG : renvoie le champ NetAddress !
```

(conversion implicite `NetAddress → NetIdentity` ⇒ `Identity.SteamId` vaut
toujours `0`/défaut). Corrigé par le commit `36e5e50` « Fixed
ConnectionInfo.Identity returning wrong thing » du **2020-03-02**, soit 3 jours
APRÈS la coupe de 2.3.2 et du NuGet 2.3.3 — **premier tag contenant le fix :
2.4.0 (janv. 2025)**. Une DLL buildée depuis master après mars 2020 est saine ;
un build NuGet 2.3.3 ne l'est pas.

Conséquence dans le fichier actuel : dans `HostSocket.OnConnected`,
`info.Identity.SteamId.Value` peut valoir 0 → `MapConnection`/`PeerConnected`
cassés. En revanche **l'`identity` reçue dans `OnMessage` est fiable même en
2.3.2** (elle vient du champ `NetMsg.Identity`, marshalé correctement).

Correction robuste (fonctionne sur toutes les versions) :
- dans `OnConnected`, ne pas se fier à `info.Identity` ; enregistrer la
  connexion avec steamId 0 (ou différer),
- faire le `MapConnection(connection.Id, identity.SteamId.Value)` au premier
  `OnMessage` (et/ou au message `Hello`, déjà prévu par le protocole), et ne
  lever `PeerConnected` qu'à ce moment-là côté hôte.

Pour trancher : ouvrir `Facepunch.Steamworks.Win64.dll` du jeu dans ILSpy/dnSpy
et regarder le getter `ConnectionInfo.Identity` (`identity` = OK,
`address` = version buggée), ou la FileVersion de la DLL.

### C4 — Vérifier le résultat de `lobby.Join()`

`Lobby.Join()` renvoie `Task<RoomEnter>` (`RoomEnter.Success` = OK, tout le
reste = échec : `Full`, `Banned`, `Error`…). Le code actuel fait `await
lobby.Join();` sans tester. Ajouter :

```csharp
var result = await lobby.Join();
if (result != RoomEnter.Success) { Status($"Échec d'entrée dans le lobby : {result}"); return; }
```

(`OnLobbyEntered` n'est de toute façon déclenché par Steam qu'en cas de
succès, mais sans ce test l'échec est totalement silencieux.)

### C5 — Pomper aussi les callbacks Steam dans `Pump()` (à vérifier côté jeu)

`Receive()` ne délivre QUE les messages de données. Les transitions
`OnConnecting/OnConnected/OnDisconnected` (socket ET connexion) ainsi que TOUS
les événements lobby/amis passent par le **Dispatch de callbacks Steam**
(`SteamNetConnectionStatusChangedCallback_t`, etc.), pompé par
`SteamClient.RunCallbacks()`.

- Si Big Ambitions initialise via `SteamClient.Init(appid)` avec le paramètre
  par défaut `asyncCallbacks = true`, une boucle `async` interne appelle
  `Dispatch.Frame()` toutes les 16 ms (continuations postées sur le main thread
  Unity via le SynchronizationContext) → rien à faire.
- Sinon, il FAUT appeler `SteamClient.RunCallbacks()` chaque frame.
- Appeler `SteamClient.RunCallbacks()` en début de `Pump()` est **sans danger
  dans les deux cas** : `Dispatch.Frame` est protégé contre la réentrance
  (`runningFrame`) et l'appel garantit l'exécution sur le main thread.
  Recommandé.

### C6 — Mineur / recommandations

- `ClientConnection.OnConnected/OnDisconnected` : les `base` ne font
  qu'invoquer une `Interface` nulle → pas de bug, mais appeler `base.` reste
  plus sûr vis-à-vis des versions futures.
- `HostSocket.OnConnecting` : `connection.Accept()` est exactement le
  comportement par défaut du `base`. C'est ici qu'on refusera les connexions
  au-delà de `MaxPlayers` (`if (Connected.Count >= MaxPlayers - 1)
  connection.Close(); else connection.Accept();`).
- Double `PeerDisconnected` côté hôte : un départ déclenche à la fois
  `OnLobbyMemberLeave` et `HostSocket.OnDisconnected` → `PlayerLeft` envoyé
  deux fois. Bénin (`RemoveRemotePlayer` est idempotent) mais à dédupliquer.
- `SendToAll` côté client peut être appelé avant `OnConnected` : les messages
  `Reliable` sont mis en file par SteamNetworkingSockets, mais les
  `Unreliable` peuvent être jetés pendant le handshake. Option : tester
  `_clientConnection.Connected` (champ `bool` public de `ConnectionManager`).
- `StartHost` et `OnGameLobbyJoinRequested` sont `async void` : envelopper le
  corps dans `try/catch` (sinon exception avalée/plantage du thread pool).
- `conn.SendMessage(...)` renvoie un `Result` (ex. `Result.LimitExceeded` si
  le buffer d'envoi de 512 Ko est plein) : au minimum le logger.
- Ne pas se fier aux paramètres `messageNum`/`recvTime` de `OnMessage` : dans
  TOUTES les versions, le site d'appel interne passe les valeurs **inversées**
  (`OnMessage(..., msg.RecvTime, msg.MessageNumber, ...)` pour une signature
  `(..., long messageNum, long recvTime, ...)`). Le fichier les ignore : parfait.

---

## Compatibilité de version (DLL du jeu potentiellement ancienne)

Tags du repo : 2.3.0/2.3.2 (févr. 2020) → **rien pendant 5 ans** → 2.4.0/2.4.1
(janv. 2025) → 2.5.0–2.5.2 (2026). NuGet s'arrête à **2.3.3 (28/02/2020)**.
Un jeu Unity sorti en 2023-2024 embarque donc soit le NuGet 2.3.3, soit un
build maison de master ; à vérifier dans la DLL (voir C3).

**Tout ce qu'utilise `SteamTransport.cs` existe déjà en 2.3.2** (y compris
`CreateRelaySocket<T>`, `ConnectRelay<T>`, tous les événements lobby, toutes
les signatures d'override). Différences pertinentes :

| API | 2.3.2 / NuGet 2.3.3 | master / ≥ 2.4.0 | Impact mod |
|---|---|---|---|
| `ConnectionInfo.Identity` | **BUGGÉ** (renvoie `address`) | corrigé (commit `36e5e50`, 2020-03-02) | voir C3 |
| `SocketManager.Connected/Connecting` | `List<Connection>` | `HashSet<Connection>` | aucun (`foreach` OK) |
| `SocketManager.Receive` | `void Receive(int bufferSize = 32)` | `int Receive(int bufferSize = 32, bool receiveToEnd = true)` | aucun |
| `ConnectionManager.Receive` | `void Receive(int bufferSize = 32)` | `int Receive(int = 32, bool = true)`, **max 256** sinon exception | aucun |
| `ConnectionManager.Close` | `Close()` sans paramètre | `Close(bool linger = false, int reasonCode = 0, string debugString = …)` | aucun (appel sans args) |
| `Connection.SendMessage` | pas de `laneIndex`; passe par `SendMessageToConnection` | + `ushort laneIndex = 0`; copie via BufferManager + `SendMessages` | aucun (appel à 2 args) |
| `Connection` `==`/`Equals` | absents | présents (`IEquatable`) | aucun |
| `ConnectionManager.SendMessages` (broadcast), `Connection.QuickStatus`, `ConfigureConnectionLanes`, lanes | absents | ≥ 2.4.0 | ne pas utiliser si DLL ancienne |
| `SteamNetworkingSockets.Identity`, FakeIP (`RequestFakeIP`, `CreateRelaySocketFakeIP`…) | absents | ≥ 2.4.0 | ne pas utiliser |
| Surcharges à interface (`CreateRelaySocket(int, ISocketManager)`, `ConnectRelay(SteamId, int, IConnectionManager)`) | absentes (seules les génériques `<T>` existent) | ≥ 2.4.0 | le mod utilise les génériques : OK |

Règle pratique : **compiler le mod contre la DLL extraite du jeu**
(`Big Ambitions_Data/Managed/Facepunch.Steamworks.Win64.dll`), jamais contre le
NuGet ni contre master — c'est la seule façon d'avoir les bonnes signatures au
binding près.

---

## Antisèche API (signatures vérifiées sur le source)

### SteamClient (`static class`, ns `Steamworks`)
```csharp
static bool   IsValid   { get; }                  // = initialisé
static SteamId SteamId  { get; }                  // SteamId du joueur local
static string Name      { get; }                  // pseudo, jamais null
static bool   IsLoggedOn { get; }
static void   Init(uint appid, bool asyncCallbacks = true);  // le jeu l'a déjà fait
static void   RunCallbacks();                     // pompe Dispatch (réentrance protégée)
```
`SteamId` est un struct : `public ulong Value;` + `bool IsValid`.
Conversions implicites `ulong ↔ SteamId`.

### SteamMatchmaking (`static` events / méthodes)
```csharp
static Task<Lobby?> CreateLobbyAsync(int maxMembers = 100);  // lobby créé INVISIBLE
static Task<Lobby?> JoinLobbyAsync(SteamId lobbyId);
static event Action<Lobby>                 OnLobbyEntered;         // soi-même (hôte inclus)
static event Action<Result, Lobby>         OnLobbyCreated;
static event Action<Friend, Lobby>         OnLobbyInvite;
static event Action<Lobby, Friend>         OnLobbyMemberJoined;
static event Action<Lobby, Friend>         OnLobbyMemberLeave;
static event Action<Lobby, Friend>         OnLobbyMemberDisconnected;
static event Action<Lobby, Friend, Friend> OnLobbyMemberKicked;    // 3e = kickeur
static event Action<Lobby>                 OnLobbyDataChanged;
static event Action<Lobby, Friend>         OnLobbyMemberDataChanged;
static event Action<Lobby, Friend, string> OnChatMessage;
```

### Lobby (struct, ns `Steamworks.Data`)
```csharp
SteamId Id { get; }                          // setter internal
Task<RoomEnter> Join();                      // Success sinon échec (Full, Banned, Error…)
void  Leave();
bool  InviteFriend(SteamId steamid);         // invite directe sans overlay
int   MemberCount { get; }
IEnumerable<Friend> Members { get; }
string GetData(string key);                  // "" si absent
bool  SetData(string key, string value);     // key ≤ 255, value ≤ 8192 (sinon throw)
bool  DeleteData(string key);
bool  SetPublic() / SetPrivate() / SetInvisible() / SetFriendsOnly();
bool  SetJoinable(bool b);
int   MaxMembers { get; set; }               // ≤ 250, owner seulement
Friend Owner { get; set; }                   // get : new Friend(GetLobbyOwner(Id))
bool  IsOwnedBy(SteamId k);
bool  SendChatString(string message);
```

### SteamFriends
```csharp
static event Action<Lobby, SteamId> OnGameLobbyJoinRequested;  // clic "Rejoindre" ami
static void OpenGameInviteOverlay(SteamId lobby);              // overlay d'invitation
static IEnumerable<Friend> GetFriends();                       // FriendFlags.Immediate
static Task<Data.Image?> GetSmallAvatarAsync(SteamId);         // 32×32
static Task<Data.Image?> GetMediumAvatarAsync(SteamId);        // 64×64
static Task<Data.Image?> GetLargeAvatarAsync(SteamId);         // 184×184
```
`Friend` : `public SteamId Id;` (champ), `string Name { get; }`, `bool IsOnline`,
`bool IsPlayingThisGame`, … `Data.Image` : `uint Width, Height; byte[] Data`
(RGBA 8888 → `Texture2D.LoadRawTextureData` + flip vertical).

### SteamNetworkingUtils
```csharp
static void InitRelayNetworkAccess();                       // async côté Steam, appeler TÔT
static SteamNetworkingAvailability Status { get; }          // état du relay (Current = prêt)
static event Action<NetDebugOutput, string> OnDebugOutput;  // + ConnectionDebugLevel
static int ConnectionTimeout { get; set; }                  // ms, après établissement
static int SendBufferSize { get; set; }                     // défaut 524288 (512 Ko)
```

### SteamNetworkingSockets
```csharp
static T CreateRelaySocket<T>(int virtualport = 0)              where T : SocketManager, new();
static T ConnectRelay<T>(SteamId serverId, int virtualport = 0) where T : ConnectionManager, new();
static event Action<Connection, ConnectionInfo> OnConnectionStatusChanged;
```

### SocketManager (classe hôte)
```csharp
HashSet<Connection> Connecting;   // List<Connection> en 2.3.2
HashSet<Connection> Connected;    //   idem
Socket Socket { get; }
bool Close();
int  Receive(int bufferSize = 32, bool receiveToEnd = true);   // void en 2.3.2
virtual void OnConnecting  (Connection connection, ConnectionInfo info); // base : Accept()
virtual void OnConnected   (Connection connection, ConnectionInfo info); // base : SetConnectionPollGroup ← OBLIGATOIRE
virtual void OnDisconnected(Connection connection, ConnectionInfo info); // base : Close() (+ retrait des listes en 2.3.2)
virtual void OnMessage(Connection connection, NetIdentity identity, IntPtr data,
                       int size, long messageNum, long recvTime, int channel);
```

### ConnectionManager (classe client)
```csharp
Connection Connection;                    // champ public
ConnectionInfo ConnectionInfo { get; }    // dernier état reçu
bool Connected;   bool Connecting;        // champs publics
void Close(bool linger = false, int reasonCode = 0, string debugString = "…"); // Close() en 2.3.2
int  Receive(int bufferSize = 32, bool receiveToEnd = true);   // bufferSize ∈ [1,256] (master)
virtual void OnConnecting  (ConnectionInfo info);
virtual void OnConnected   (ConnectionInfo info);
virtual void OnDisconnected(ConnectionInfo info);
virtual void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel);
```

### Connection (struct)
```csharp
uint Id { get; set; }
Result Accept();
bool   Close(bool linger = false, int reasonCode = 0, string debugString = "Closing Connection");
Result SendMessage(IntPtr ptr, int size, SendType sendType = SendType.Reliable, ushort laneIndex = 0);
Result SendMessage(byte[] data, SendType sendType = SendType.Reliable, ushort laneIndex = 0);
Result SendMessage(byte[] data, int offset, int length, SendType sendType = SendType.Reliable, ushort laneIndex = 0);
Result SendMessage(string str, SendType sendType = SendType.Reliable, ushort laneIndex = 0); // "ton of garbage"
Result Flush();
// laneIndex absent en 2.3.2 ; QuickStatus()/ConfigureConnectionLanes() : ≥ 2.4.0 seulement
```

### SendType (`[Flags] enum : int`, ns `Steamworks.Data`)
```csharp
Unreliable = 0,  NoNagle = 1,  NoDelay = 4,  Reliable = 8
// NoDelay : uniquement valable sur de l'Unreliable
// Reliable : jusqu'à 512 Ko par message (fragmentation gérée)
```

### ConnectionInfo / NetIdentity
```csharp
ConnectionInfo.State     → ConnectionState (Connecting/Connected/ClosedByPeer/ProblemDetectedLocally/None…)
ConnectionInfo.Identity  → NetIdentity     // ⚠ buggé en ≤ 2.3.3 (voir C3)
ConnectionInfo.EndReason → NetConnectionEnd
NetIdentity.SteamId      → SteamId         // default si l'identité n'est pas un SteamID
NetIdentity.IsSteamId    → bool
// conversions implicites SteamId ↔ NetIdentity
```

---

## Patterns et pièges vérifiés

### Pompage des messages
Le pattern du fichier est le bon : chaque frame,
`_hostSocket?.Receive(32)` + `_clientConnection?.Receive(32)`.
- `SocketManager.Receive` lit sur le **poll group** (d'où C1) et rappelle
  récursivement tant que le buffer est plein.
- `ConnectionManager.Receive` lit sur la connexion ; en master, `bufferSize`
  > 256 jette une exception.
- Le pointeur `data` passé à `OnMessage` est **libéré dès le retour** du
  callback (`NetMsg.InternalRelease`) → la copie `Marshal.Copy` de
  `CopyPayload` est obligatoire et correcte.
- Ne jamais appeler `Receive` depuis un callback Steam.

### RunCallbacks
Voir C5. Résumé : les données passent par `Receive()`, mais **tout le reste
(états de connexion, lobby, invitations, résultats `async`) passe par
`SteamClient.RunCallbacks()`** — automatique si le jeu a laissé
`asyncCallbacks = true` dans `SteamClient.Init`, sinon à faire soi-même.
Un appel redondant par frame est inoffensif (garde anti-réentrance).

### Initialisation du relay
`InitRelayNetworkAccess()` est asynchrone côté Steam : l'appeler **au
chargement du mod**, pas au moment d'héberger/rejoindre. Surveiller
`SteamNetworkingUtils.Status` (`SteamNetworkingAvailability.Current` = prêt).
La première connexion relay peut prendre 1-3 s (négociation SDR + ticket).

### Lobby vs ancien P2P
Le lobby Steam n'est qu'un annuaire/salon (métadonnées + invitations) : il ne
transporte pas les données de jeu. Le transport est bien
SteamNetworkingSockets (`CreateRelaySocket`/`ConnectRelay`). Ne PAS mélanger
avec la classe legacy `SteamNetworking` (`SendP2PPacket`, `OnP2PSessionRequest`,
canaux int) : autre pile, autre NAT traversal, dépréciée. Un
`CreateLobbyAsync` renvoie un lobby **Invisible** — `SetFriendsOnly()` est
indispensable (le fichier le fait) pour que « Rejoindre la partie » marche
depuis la liste d'amis.

### Tailles et canaux
- `Reliable` : max **512 Ko** par message (fragmentation/réassemblage gérés).
- `Unreliable` : peut dépasser un MTU (~1200 octets utiles) mais le message
  entier est perdu si un fragment l'est → garder les états 10 Hz < 1 Ko.
- Buffer d'envoi : 512 Ko par défaut (`SendBufferSize`) ; plein →
  `Result.LimitExceeded`.
- Il n'y a **pas de canaux** dans `Connection.SendMessage` (le paramètre
  `channel` de `OnMessage` reste 0) ; les « lanes » (`laneIndex`,
  `ConfigureConnectionLanes`) n'existent qu'à partir de 2.4.0. Multiplexer via
  l'octet de type de `NetMessage`, comme c'est déjà fait.
- Rappel : les valeurs `messageNum`/`recvTime` reçues sont interverties par la
  lib (toutes versions) — ne pas s'en servir.

### Pour la suite du mod
- **Liste d'amis** : `SteamFriends.GetFriends()` (+ `friend.IsPlayingThisGame`)
  pour une UI d'invitation maison, avec `lobby.InviteFriend(friend.Id)` sans
  passer par l'overlay.
- **Avatars** : `await SteamFriends.GetMediumAvatarAsync(steamId)` →
  `Data.Image` RGBA → `Texture2D` (au-dessus des avatars distants).
- **Chat de lobby** : `lobby.SendChatString` + `SteamMatchmaking.OnChatMessage`
  — pratique pré-partie, sans toucher au transport.
- **Voice** (présent dès 2.3.2, classe `SteamUser`) :
  `SteamUser.VoiceRecord = true`, `HasVoiceData`, `ReadVoiceDataBytes()` →
  envoyer en `Unreliable`, côté réception
  `DecompressVoice(Stream input, int length, Stream output)` → PCM mono 16 bits
  (`SampleRate` réglable 11025–48000, `OptimalSampleRate`).
- **Diagnostic** : `SteamNetworkingUtils.OnDebugOutput` +
  `ConnectionDebugLevel = NetDebugOutput.Msg` ; `connection.Flush()` pour
  court-circuiter Nagle sur un message urgent.
