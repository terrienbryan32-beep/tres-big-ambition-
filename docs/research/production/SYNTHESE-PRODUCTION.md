# Synthèse production — comment on fabrique CoopAmbitions confortablement

*Croisement des 5 rapports du dossier `docs/research/production/` (2026-08-31) :
[Outillage de dev](outillage-dev.md) · [Harmony expert](harmony-expert.md) ·
[CI/CD & releases](ci-cd-releases.md) · [Études de cas](etudes-de-cas.md) ·
[Qualité, debug & UX](qualite-debug-ux.md)*

---

## 1. La boucle de dev quotidienne (le gain n°1)

- **Build externe `dotnet` façon Dudeldups** : son script `BuildBigAmbitionsMods.ps1`
  (cloné, à adapter en `tools/build.ps1`) compile un mod BA en ~5 s hors Unity
  (csproj net472 généré : refs GameDlls + UnityEngine de l'éditeur, defines Unity,
  install directe dans `ModsLocal` avec retry). Unity ne sert plus qu'au manifest,
  aux bundles et au build canonique de release (Mod Builder = validation des 13 règles).
- **Hot reload** : le jeu active/désactive les mods à chaud (menu Mods →
  `OnLoadAsync`/`OnUnloadAsync`). Première expérience à faire : vérifier si un toggle
  relit la DLL du disque. Tout ce qui rend l'unload propre rend aussi le reload fiable.
- **Junction NTFS** : le dossier `CoopAmbitions/` de ce repo est lié dans
  `Assets/Mods/` du SDK (`tools/link-mod.ps1`) ; les `.meta` et le `ModManifest.asset`
  vivent dans NOTRE repo à travers la junction. Le clone du SDK reste jetable.
- **Objectif mesurable** : du `git commit` à « deux avatars qui bougent » en < 60 s
  sans toucher un menu (dev.json : `autoLoadSave`, `autoHost`, `useLoopbackTransport`).
- **Debug** : dnSpyEx + le mono-debug de la version exacte (2022.3.62f2) sur une COPIE
  du dossier du jeu ; UnityExplorer Standalone dans un mod compagnon local `CoopDevTools`
  (jamais publié) ; `tail-log.ps1` sur le Player.log filtré `[Coop.`.

## 2. Harmony (décision ferme)

- **Embarquer `Lib.Harmony` 2.4.2 « fat », build net472 → un seul `0Harmony.dll`**
  dans `Dependencies/` (MonoMod fusionné). HarmonyX écarté : 4-6 DLL sœurs dans un
  dossier plat = risques de collision inter-mods.
- Organisation **manuelle façon Nitrox** : un fichier par patch, cible résolue par
  lambda typée (refactoring-safe), try/catch par patch — jamais `PatchAll` global
  (un patch cassé tuerait tout le lot).
- `UnpatchSelf()` dans `OnUnloadAsync` (hot reload et hygiène).
- **PacketSuppressor** (compteur par type, RAII, main-thread only) posé AVANT le
  premier patch émetteur.
- Taxonomie des patches coop : *intention* (invité → requête à l'hôte),
  *résultat* (hôte → broadcast), *neutralisant* (pause locale interdite, save invité coupée).
- 10 premières cibles (ordre) : `ChangeMoneySafe`, tick du temps, pause, save,
  achat bâtiment, placement de meubles, véhicules, employés, ticks économiques, clients IA.

## 3. CI/CD et releases

- **v0 (tout de suite, zéro friction)** : tests unitaires du protocole (`NetMessage`
  round-trip — exécutable en .NET pur avec le NuGet `UnityEngine.Modules`), format/lint,
  watcher de buildid Steam en cron (issue automatique « le jeu a été mis à jour »),
  `docs/VERSIONS.md` (commit SDK + buildid + versions testées), `COMPATIBILITY.md`.
- **v1 (première release publique)** : reference assemblies strippées **privées**
  (Refasmer — précédent légal : RimWorld avec permission de Ludeon, Stardew, Lethal
  Company ; **demander la permission à Hovgaard d'abord**, ils sont mod-friendly),
  job de compilation Linux façon Dudeldups, release tag-driven (tag == version du
  manifest, git-cliff pour le changelog, zip au format `Output/` du SDK).
- **Workshop** : création de l'item in-game (obligatoire), mises à jour automatisables
  ensuite via `steamcmd +workshop_build_item` (à tester sur un item jetable).
- Unity headless en CI : possible mais **overkill** tant que le mod est code-only.

## 4. Qualité, debug multijoueur, UX

- **Tester à deux** : la vraie réponse = 2e copie du jeu sur un compte secondaire
  (family sharing insuffisant : 1 copie = 1 joueur simultané). Mais 90 % du dev se
  fait SANS ça : le **LoopbackTransport** (2 sessions en mémoire, même process) —
  c'est pour ça qu'on a extrait `ICoopTransport`.
- **Latence/perte simulées gratuites** : `SteamNetworkingUtils.FakeSendPacketLag/Loss`
  existe dans la lib embarquée par le jeu (vérifié dans le source Facepunch).
- **Overlay réseau F8** : ping, qualité, pkt/s, congestion — tout vient de
  `Connection.QuickStatus()`, quasi gratuit. + registre de debuggers IMGUI `#if DEBUG`
  façon Nitrox (F7).
- **Erreurs actionnables** : codes d'erreur documentés (CA-Exx), message qui dit QUI
  doit agir (« l'hôte doit mettre à jour », « vérifie ton firewall »), panneau
  troubleshooting différencié hôte/invité (pattern CSM), bouton « report » qui zippe
  les logs (avant release).
- **UI** : IMGUI pour le debug ; `OptionsService` natif du SDK pour les réglages ;
  prefab AssetBundle Canvas pour l'UX joueur (piège TMP : réassigner la police du jeu).

## 5. Les invariants des mods qui durent (études de cas)

Ce que font TOUS les projets qui ont survécu 5+ ans (SMAPI, tModLoader, Nitrox,
Jotunn, Combat Extended, TM:PE, BepInEx) :

1. **Tampon d'isolation** entre le code du mod et le jeu (nos classes `Interop/`,
   les façades SMAPI, le `Reflect` Nitrox) — la MAJ du jeu ne casse qu'une couche.
2. **La MAJ du jeu est un processus planifié**, pas une surprise : détection
   (watcher buildid), tests qui hurlent (delta d'IL des patches façon Nitrox),
   notes de portage versionnées (tModLoader).
3. **Double canal de distribution** (Workshop + GitHub Releases) et canaux
   STABLE/TEST (TM:PE) — d'autant plus que Going Public semble avoir été retiré
   du Workshop.
4. **Handshake versionné** avec refus actionnable (le `NetworkCompatibilityAttribute`
   de Jotunn est le modèle à copier).
5. **Le diagnostic est un produit** : log parser web de SMAPI, bouton report,
   messages d'erreur écrits pour l'utilisateur final.
6. **Le savoir vit dans l'outillage**, pas dans la tête du mainteneur : scripts,
   générateurs, analyzers, docs de processus.
7. **Propriété collective** : licence libre + organisation GitHub (pas un compte
   perso), page Workshop sur un compte dédié (TM:PE a perdu 2 pages Workshop dans
   des successions de mainteneurs), bus factor > 1.
8. **Une rampe pour les contributeurs** : CONTRIBUTING, bonnes premières issues,
   Discord.

Les pièges qui tuent : facteur bus = 1, closed source (mort de l'auteur = mort du
mod), la « v2 réécriture éternelle », le couplage diffus au jeu, le burn-out du
mainteneur unique, la page Workshop personnelle.

## 6. Déjà appliqué au code dans ce commit

| Changement | Pourquoi |
|---|---|
| `ICoopTransport` extrait, `CoopSession` découplé | testable sans Steam ni le jeu |
| `LoopbackTransport` (hôte + N invités en mémoire) | développer le protocole en solo, base des futurs tests |
| `Debug/CoopLog.cs` (tags `[Coop.*]`, `WarnOnce`, verbosité) | logs grep-ables dans le Player.log, pas de spam |

## 7. File d'attente de production (ordre de rentabilité)

1. `tools/link-mod.ps1` + `tools/build.ps1` (adapter Dudeldups) + `tools/run.ps1` + `tail-log.ps1`
2. Expérience hot-reload (documenter dans `VERSIONS.md`)
3. Tests round-trip de `NetMessage` (+ CI GitHub Actions v0 : tests + watcher buildid)
4. `dev.json` (autoHost/autoJoin/loopback) + save de test
5. Overlay réseau F8 + registre debug F7
6. `Dependencies/0Harmony.dll` + `PatchManager` + `Suppressor<T>` au moment du premier patch
7. Avant release : codes d'erreur CA-Exx, bouton report, `package.ps1`, refs strippées + CI de compilation (avec l'accord de Hovgaard)
