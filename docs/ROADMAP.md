# Roadmap

> Voir [research/SYNTHESE.md](research/SYNTHESE.md) pour les décisions d'architecture
> et [research/production/SYNTHESE-PRODUCTION.md](research/production/SYNTHESE-PRODUCTION.md)
> pour l'outillage, Harmony, la CI/CD et la qualité (avec file d'attente priorisée).

## Phase 0 — Fondations ✅ (ce dépôt)
- [x] Structure de mod conforme au SDK officiel (asmdef, locales, point d'entrée `IModBigAmbitions`)
- [x] Transport Steam : lobby + sockets relay (Facepunch.Steamworks) — vérifié contre le source Facepunch
- [x] Protocole binaire versionné (Hello/Welcome/PlayerState/PlayerLeft)
- [x] Avatar distant avec interpolation
- [x] `LocalPlayerLocator` branché sur `GameManager.Instance.playerController` (accès attesté)
- [x] Dossier de recherche complet (`docs/research/` + `docs/research/production/`)
- [x] `ICoopTransport` + `LoopbackTransport` (dev/test du protocole sans Steam) + `CoopLog`
- [ ] Outillage : `tools/link-mod.ps1`, `build.ps1` (adaptation Dudeldups), `run.ps1`, `tail-log.ps1`
- [ ] Compiler dans Unity avec les DLL du jeu
- [ ] Créer le `ModManifest.asset` dans l'éditeur Unity
- [ ] Démarrer le réseau sur `GlobalEvents.RegisterOnGameLoadedCallback` plutôt qu'à l'init
- [ ] Tests round-trip de `NetMessage` + CI v0 (tests, watcher de buildid Steam)

## Phase 1 — MVP « se voir » 
- [ ] Tester à deux (F9 héberger → invitation Steam → connexion)
- [ ] Session dnSpy guidée (liste ordonnée dans research/internals-du-jeu.md §finale)
- [ ] Remplacer la capsule par un vrai modèle de personnage (asset bundle ou clone du prefab joueur)
- [ ] Synchroniser l'animation de base (idle/marche/course) et l'état « en véhicule »
- [ ] UI minimale : état de connexion, liste des joueurs (ModOptions ou petit overlay)

## Phase 2 — Horloge partagée
- [ ] Identifier le service de temps du jeu et le forcer côté client
- [ ] `TimeState` hôte → clients (1 Hz) + rattrapage doux
- [ ] Gérer pause/sommeil/skip : consensus « tout le monde dort » côté hôte
- [ ] Bloquer les contrôles de temps côté client

## Phase 3 — Monde partagé
- [ ] Transfert de la sauvegarde de l'hôte au join
- [ ] Répliquer les événements monde un par un, dans cet ordre de valeur :
  - [ ] argent / transactions bancaires
  - [ ] achats de meubles + placement (InteriorDesigner)
  - [ ] location/achat d'adresses
  - [ ] véhicules (position quand conduit, propriété)
  - [ ] employés et logistique
- [ ] Embarquer HarmonyX en dépendance si les hooks natifs manquent

## Phase 4 — Confort
- [ ] Reconnexion automatique, gestion propre des déconnexions
- [ ] Chat texte (ou intégration voice Steam)
- [ ] 3-4 joueurs, réglages de lobby (public/amis)
- [ ] Publication sur le Steam Workshop via le Mod Creator in-game
