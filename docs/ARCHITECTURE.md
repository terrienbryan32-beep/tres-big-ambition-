# Architecture du mod coop

## 1. Modèle : hôte autoritaire

Un joueur (**l'hôte**) fait tourner la « vraie » partie : c'est sa sauvegarde, son horloge,
son économie. Les autres joueurs (**clients**) se connectent à lui et voient/agissent dans
son monde. C'est le modèle le plus simple et le plus robuste pour un jeu qui n'a jamais
été pensé multijoueur :

- pas de fusion de sauvegardes : on charge celle de l'hôte;
- en cas de conflit, l'état de l'hôte gagne toujours;
- un client qui se déconnecte ne casse rien.

## 2. Transport réseau : Steam relay

Le jeu embarque **Facepunch.Steamworks** et tourne avec Steam déjà initialisé. On réutilise ça :

```
Hôte                                Client
----                                ------
SteamMatchmaking.CreateLobbyAsync   accepte l'invitation Steam
lobby.SetFriendsOnly()              SteamFriends.OnGameLobbyJoinRequested
OpenGameInviteOverlay               lobby.Join() → lit lobby.Owner
SteamNetworkingSockets              SteamNetworkingSockets
  .CreateRelaySocket<HostSocket>      .ConnectRelay<ClientConnection>(ownerId)
```

Avantages : NAT traversal gratuit (relay Valve), invitations par l'overlay Steam,
identité des joueurs (SteamId, pseudo, avatar) fournie.

Les deux extrémités « pompent » les messages à chaque frame (`Receive()`), via le
`CoopRunner` (un `MonoBehaviour` créé par le mod, marqué `DontDestroyOnLoad`).

## 3. Protocole

Paquets binaires simples (`BinaryWriter`/`BinaryReader`), un octet d'en-tête pour le type :

| Type            | Sens            | Fréquence     | Contenu |
|-----------------|-----------------|---------------|---------|
| `Hello`         | client → hôte   | 1× à la connexion | version protocole, SteamId, pseudo |
| `Welcome`       | hôte → client   | 1× en réponse | version, liste des joueurs présents |
| `PlayerState`   | les deux sens   | ~10 Hz        | position, cap (yaw), vitesse |
| `PlayerLeft`    | hôte → clients  | événement     | SteamId |
| `TimeState`     | hôte → clients  | ~1 Hz (phase 2) | jour, heure, échelle de temps |
| `WorldEvent`    | les deux sens   | événement (phase 3) | action répliquée (achat, placement…) |

Règles :
- **Version de protocole** vérifiée dans `Hello`/`Welcome` : refus si différente.
- `PlayerState` en **non-fiable** (unreliable) — une position perdue est remplacée par la suivante.
- Tout le reste en **fiable** (reliable).
- L'hôte **relaie** les `PlayerState` des clients aux autres clients (topologie en étoile).

## 4. Synchronisation du joueur (phase 1 — MVP)

- `LocalPlayerLocator` retrouve le `Transform` du personnage local. Sans les DLL du jeu
  sous la main, le code utilise des heuristiques (tag `Player`, noms de GameObject,
  caméra) — **à remplacer** par l'accès direct au service du jeu une fois dans Unity
  (chercher dans `BigAmbitions.Characters` / `Services` un singleton type
  `PlayerCharacterService` ou équivalent).
- Envoi à 10 Hz seulement si la position a bougé (seuil ~1 cm) : quasi aucun trafic à l'arrêt.
- Côté réception, `RemotePlayerView` crée un avatar (capsule + pseudo en tête) et
  **interpole** entre les deux derniers états reçus (~150 ms de retard volontaire)
  pour un mouvement fluide malgré les 10 Hz.

## 5. Horloge partagée (phase 2)

Le temps de Big Ambitions (accélération, sommeil, skip) est le premier vrai point de
friction du coop :

- L'hôte envoie `TimeState` à ~1 Hz; le client **force** son horloge locale à suivre
  (via le service de temps du jeu — à identifier dans les DLL, probablement dans
  `BigAmbitions.dll` ou `DayNightCycle.dll`).
- Côté client, on neutralise pause / accélération / sommeil (ou on en fait des
  *requêtes* envoyées à l'hôte : « tout le monde dort → skip »).

## 6. Monde partagé (phase 3)

Plutôt que de synchroniser tout l'état (énorme), on réplique les **actions** :

- un joueur achète/place/vend quelque chose → `WorldEvent` sérialisé → l'hôte le valide,
  l'applique à sa partie, et le rediffuse aux autres clients qui l'appliquent aussi;
- la sauvegarde reste celle de l'hôte; à la connexion, le client charge une **copie**
  de la sauvegarde de l'hôte (transfert au join) pour partir du même état.

C'est le même patron que la plupart des mods coop de jeux solo (lockstep d'événements,
hôte juge de paix). Chaque type d'action du jeu devra être accroché un par un
(Harmony/hooks ou API du ModAPI si elle expose des événements).

## 7. Points de vigilance

- **ModAPI officiel vs hooks** : le SDK officiel charge le mod et donne accès aux DLL du
  jeu, mais n'expose pas (encore) d'API d'événements complète. Pour intercepter des
  actions du jeu (phase 3), il faudra soit trouver les événements C# publics des
  services du jeu, soit embarquer HarmonyX comme dépendance locale du mod
  (`Dependencies/` dans le SDK).
- **Déterminisme** : ne jamais laisser deux simulations « calculer chacune dans leur
  coin » (IA clients, trafic, économie divergeront). Tout ce qui compte vient de l'hôte.
- **Triche/robustesse** : c'est du coop entre amis — l'hôte valide, mais pas besoin
  d'anti-triche.
- **Versions du jeu** : chaque mise à jour de Big Ambitions peut casser les accroches;
  isoler tous les accès au jeu dans des classes dédiées (`LocalPlayerLocator`, futurs
  adaptateurs) pour limiter la casse.
