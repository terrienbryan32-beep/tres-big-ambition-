# CoopAmbitions — Mod coopératif pour Big Ambitions

Un mod multijoueur coopératif pour [Big Ambitions](https://store.steampowered.com/app/1331550/Big_Ambitions/) (Hovgaard Games), construit sur le **SDK de modding officiel** du jeu.

> ⚠️ Projet en cours de développement. Le squelette est fonctionnel dans sa structure,
> mais il doit être compilé et itéré dans Unity avec les DLL du jeu (voir ci-dessous).

## Pourquoi c'est possible

- Big Ambitions est un jeu **Unity (backend Mono)** : les assemblies du jeu sont des DLL C# managées, directement référençables par un mod.
- Hovgaard Games publie un [SDK de modding officiel](https://github.com/hovgaardgames/bigambitions) : les mods sont des assemblies C# chargées par le jeu via `BAModAPI` (`IModBigAmbitions`), et distribuées via le **Steam Workshop** directement depuis le jeu.
- Le jeu embarque **Facepunch.Steamworks** (`Facepunch.Steamworks.Win64.dll` fait partie des DLL exposées aux mods) : on peut créer des lobbies Steam et communiquer via le réseau relay de Steam (`SteamNetworkingSockets`) — pas besoin d'ouvrir des ports ni d'héberger un serveur.

À noter : un mod multijoueur communautaire existe déjà (« Going Public » sur le Workshop). Ce projet est une base pour construire le tien — regarder ce qui existe reste une bonne source d'inspiration.

## Architecture en bref

- **Hôte autoritaire** : un joueur héberge, sa partie fait foi (temps, économie, monde). Les clients rejoignent via une invitation Steam.
- **Transport** : lobby Steam (invitations entre amis) + `SteamNetworkingSockets` en mode relay.
- **Synchronisation par phases** (voir [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)) :
  1. **MVP** — voir l'autre joueur se déplacer dans la ville (avatar + interpolation) ✅ *(code de base présent)*
  2. Horloge de jeu partagée (l'hôte dicte le temps, pause/skip désactivés côté client)
  3. Économie/monde partagés (événements répliqués : achats, aménagement, véhicules…)

## Structure du dépôt

```
CoopAmbitions/                 ← le mod (à placer dans Assets/Mods/ du SDK)
  CoopAmbitions.asmdef         ← références vers les DLL du jeu
  Locales/                     ← textes en/fr
  Scripts/
    Core/CoopMod.cs            ← point d'entrée (IModBigAmbitions)
    Core/CoopRunner.cs         ← MonoBehaviour pilote (Update, raccourcis)
    Net/NetMessage.cs          ← protocole : types de paquets + sérialisation
    Net/SteamTransport.cs      ← lobby Steam + sockets relay (Facepunch)
    Net/CoopSession.cs         ← orchestration hôte/client
    Sync/LocalPlayerLocator.cs ← trouve le transform du joueur local
    Sync/RemotePlayerView.cs   ← avatar du joueur distant + interpolation
docs/
  ARCHITECTURE.md              ← conception détaillée du coop
  ROADMAP.md                   ← plan de développement par phases
```

## Mise en place (développement)

1. **Prérequis** : Unity Hub + **Unity 2022.3.62f2**, un IDE C#, Big Ambitions installé via Steam.
2. Clone le SDK officiel :
   ```
   git clone https://github.com/hovgaardgames/bigambitions
   ```
3. Ouvre le projet SDK dans Unity et suis la fenêtre de bienvenue : elle importe les **DLL du jeu** depuis ton installation Steam (`Assets/_BaDependencies/GameDlls/`).
4. Copie (ou lie) le dossier `CoopAmbitions/` de ce dépôt dans `Assets/Mods/` du projet SDK.
5. Dans Unity, crée le **ModManifest** du mod : clic droit sur le dossier `CoopAmbitions` → `Create` → manifest Big Ambitions, puis renseigne :
   - `ModId` : `CoopAmbitions`
   - `DisplayName` : `Coop Ambitions`
   - `ModAssembly` : l'asmdef `CoopAmbitions`
   - `LocalesFolder` : le dossier `Locales`
   *(le manifest est un ScriptableObject avec des GUID Unity — il doit être créé dans l'éditeur, pas à la main)*
6. **Big Ambitions → Mod Builder → Build & Install** : compile et installe le mod dans le dossier `ModsLocal` du jeu.
7. Lance le jeu, active le mod, charge une partie.

## Test en jeu (MVP)

- **F9** : héberger — crée un lobby Steam (amis seulement) et ouvre l'overlay d'invitation.
- L'ami invité accepte l'invitation Steam → son jeu rejoint le lobby et se connecte à l'hôte.
- Chacun voit l'avatar de l'autre (capsule + pseudo Steam) se déplacer dans la ville.

Il faut **deux comptes Steam** possédant le jeu pour tester en conditions réelles.

## Avertissements honnêtes

- Le code réseau et l'accroche au joueur local (`LocalPlayerLocator`) utilisent des heuristiques : les noms exacts des types du jeu (`BigAmbitions.Characters`, etc.) ne sont visibles qu'une fois les DLL importées dans Unity. Attends-toi à ajuster ces points à la compilation — les zones concernées sont marquées `// A AJUSTER`.
- Big Ambitions est conçu comme un jeu solo : le temps qui s'accélère, la pause, les sauvegardes et toute l'économie sont pensés pour un seul joueur. Un coop complet est un gros chantier — d'où l'approche par phases (voir la [roadmap](docs/ROADMAP.md)).
