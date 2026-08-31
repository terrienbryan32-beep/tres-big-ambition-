# Big Ambitions — Inventaire des mécaniques et systèmes (recherche pour le mod coop)

> Recherche effectuée le 31/08/2026. Jeu : **Big Ambitions** (Hovgaard Games, Steam appid 1331550, Unity/IL2CPP).
> **Contexte critique : la version 1.0 est sortie le 28 août 2026** (fin de l'Early Access), avec un pic historique de +12 000 joueurs simultanés. D'autres updates puis un premier DLC sont annoncés — la base de code va donc continuer à bouger.
>
> Note méthodo : le proxy réseau bloquait l'accès direct à fandom.com, bigambitionsgame.com, store.steampowered.com et steamcommunity.com ; les informations ci-dessous proviennent des extraits indexés de ces sources (liens fournis pour vérification en local).

---

## 1. Temps — le cœur du problème coop

**Fonctionnement.**
- Horloge : **1 seconde réelle = 1 minute de jeu**, soit **24 minutes réelles par journée de jeu** à vitesse normale ([discussion Steam](https://steamcommunity.com/app/1331550/discussions/0/3823034248719830506/)).
- Semaine de 7 jours (lundi→dimanche) avec vrais jours nommés ; le jour et l'heure influencent la demande client (multiplicateurs horaires/journaliers dataminés par type de business — [guide Steam « Business Hour Multipliers »](https://steamcommunity.com/sharedfiles/filedetails/?id=3695018728)).
- **Accélérations de temps** : le temps s'accélère automatiquement pendant le **sommeil** (jusqu'à 24 h d'un coup, slider d'heure de réveil) et pendant le **travail à la caisse** (« time machine » quand on s'assigne soi-même au registre) ([discussion](https://steamcommunity.com/app/1331550/discussions/0/6620895347253250963/), [guide sleeping](https://steamcommunity.com/app/1331550/discussions/0/686368595766300108/)). Pas de contrôle libre de la vitesse en vanilla (des mods comme [AutoIdle](https://www.nexusmods.com/bigambitions/mods/5) patchent le multiplicateur du `TimeMachine`).
- Pause : le jeu se met en pause dans les menus/en solo (jeu solo classique).
- **Événements planifiés sur l'horloge** :
  - Livraisons entrepôt→magasins : **tous les jours à 2 h 00** ([wiki Delivery Driver](https://big-ambitions.fandom.com/wiki/Delivery_Driver)).
  - Livraisons grossistes/importateurs : arrivée le matin / à minuit le jour du contrat ; commandes urgentes livrées le lendemain matin (+20 %) ([wiki Wholesalers](https://big-ambitions.fandom.com/wiki/Wholesalers), [Importers](https://big-ambitions.fandom.com/wiki/Importers)).
  - Frais bancaires : **chaque lundi** (0,5 %, 5 % en difficile) ([FAQ Bigger Ambitions](https://www.biggerambitions.com/faq.php)).
  - Salaires/loyers : échéances hebdomadaires ; impôts **IRS tous les 60 jours** (annuel en temps de jeu) sur revenus, loyers et gains de casino ([wiki IRS](https://big-ambitions.fandom.com/wiki/IRS)).
  - Casino boat : **ouvert uniquement le vendredi**, billets 18 h–22 h ([wiki Casino Boat](https://big-ambitions.fandom.com/wiki/Casino_Boat)).
  - Horaires d'ouverture des lieux (banques, magasins tiers) et des commerces du joueur (planning libre, éventuellement 24/7).

**État porté** : date, jour de semaine, heure/minute, multiplicateur de vitesse courant, files d'événements planifiés (livraisons, paie, impôts).
**Fréquence** : continue (chaque seconde réelle).
**Difficulté de synchro : MAXIMALE.** Deux joueurs ne peuvent pas dormir/accélérer indépendamment. Le mod multi existant [Going Public](https://steamcommunity.com/sharedfiles/filedetails/?id=3765662670) résout ça par **horloge unique côté hôte + vote unanime pour le skip de nuit** — c'est l'approche à retenir. Les hooks probables sont la classe `TimeMachine` (nom confirmé par les mods Nexus).

---

## 2. Personnage

**Besoins** : **trois seulement — Énergie, Faim, Bonheur** (pas d'hygiène) ([wiki Energy](https://big-ambitions.fandom.com/wiki/Energy), [Hunger](https://big-ambitions.fandom.com/wiki/Hunger), [Happiness](https://big-ambitions.fandom.com/wiki/Happiness)).
- Énergie : restaurée par sommeil et boissons caféinées. Faim ou bonheur à 0 % → drain d'énergie accéléré ; faim à 0 % → consommation d'énergie ×3.
- Faim : manger/boire (commerces, frigo perso).
- Bonheur : loisirs (ex. casino = +40 % pendant 3 jours), logement, etc. Influence aussi indirectement la performance.

**Compétences/XP** : **pas de système de skills ni d'XP pour le joueur** ; seules les classes de départ (« work styles ») donnent des bonus hard-codés ; le joueur à la caisse vaut un skill service client fixe de 50 % ([forum officiel](https://forum.bigambitionsgame.com/t/player-skills-levels/1291)).

**Déplacement** ([wiki Transportation](https://big-ambitions.fandom.com/wiki/Transportation)) :
- À pied ; **taxis** (héler dans la rue, destination sur la carte) ; **métro** (3 $ par trajet, stations sur la carte) ;
- **Véhicules** : 15 voitures de 2 500 $ à 720 000 $, stats (vitesse, capacité cargo, auto-park), achat en concession, **essence** (2 stations : Murray Hill, Hell's Kitchen), **réparations**, dépanneuse, **tickets de parking (150 $/3 h en zone illégale)** ([liste des voitures](https://commonsensegamer.com/big-ambitions-list-of-cars-in-the-game-with-capacity-and-auto-park-support/), [GameSkinny](https://www.gameskinny.com/tips/big-ambitions-how-to-repair-fuel-and-sell-your-car/)) ;
- **1.0** : **chauffeur privé** (contrat Onyx Luxury Chauffeurs, on dépose un véhicule dans leur garage et on l'appelle) et **livraison de repas Speedy Bites** (se faire livrer, ou travailler soi-même comme livreur) ([Steam news 1.0](https://store.steampowered.com/news/app/1331550/view/716786116904093572), [PCGamesN](https://www.pcgamesn.com/big-ambitions/1-0-tycoon-life-sim)).

**Inventaire personnel/manutention** : transport de cartons à la main, **hand truck (4 emplacements)**, **flatbed IKA Bohag (8 cartons)**, coffres de véhicules (capacité selon modèle), chargement/déchargement physique des marchandises ([discussion](https://steamcommunity.com/app/1331550/discussions/0/3825284575377299000/)).

**État porté** : 3 jauges, position/orientation, animation, véhicule courant + essence/état, contenu porté, contrats perso (chauffeur, etc.).
**Fréquence** : jauges = drain continu lent ; position = continue.
**Difficulté** : faible à moyenne. Par joueur, donc **état local à chaque client** (chacun possède son perso) ; seuls position/anim/véhicule doivent être répliqués aux autres. Piège : le drain des besoins dépend de la vitesse du temps → à harmoniser avec l'horloge partagée (Going Public expose des réglages hôte pour la vitesse de drain).

---

## 3. Économie personnelle

- **Argent liquide + compte bancaire**, visibles via l'app **EconoView**.
- **Deux banques** : Jensen Capital (plafond total de prêts **40 000 $**) et Vantander Bank (plafond **800 000 $**), avec horaires d'ouverture réels ; prêts + **placements/investissements** ([wiki Vantander](https://big-ambitions.fandom.com/wiki/Vantander_Bank), [Jensen Capital](https://big-ambitions.fandom.com/wiki/Jensen_Capital)).
- **Pas de score de crédit** : les limites sont par banque ; taux d'intérêt dépendants de la difficulté.
- **Frais bancaires hebdomadaires le lundi** (0,5 % / 5 % en difficile) — mécanique anti-thésaurisation ([FAQ](https://www.biggerambitions.com/faq.php)).
- **Impôts IRS** tous les 60 jours sur les revenus (business, loyers, casino), moins les dépenses déductibles ([wiki IRS](https://big-ambitions.fandom.com/wiki/IRS)).
- La **1.0 a remanié le banking** (prêts, investissements, impôts) ([PCGamesN](https://www.pcgamesn.com/big-ambitions/1-0-tycoon-life-sim)).
- Casino boat (vendredi) : roulette, blackjack, machines à sous, mises 1–100 000 $ — source de gros gains ([wiki Casino Boat](https://big-ambitions.fandom.com/wiki/Casino_Boat), [gamepressure](https://www.gamepressure.com/newsroom/the-casino-in-big-ambitions-easy-money-cheat/ze5261)).

**État porté** : soldes, liste de prêts (capital, taux, échéances), investissements, dette fiscale accumulée.
**Fréquence** : transactionnelle (événements discrets) + prélèvements planifiés.
**Difficulté** : moyenne. En coop « chacun son entreprise » (modèle Going Public), l'argent est **par joueur** ; il faut surtout synchroniser les **échéances** (lundi bancaire, IRS) sur l'horloge commune, et décider si les prêts/transferts entre joueurs existent (Going Public a ajouté dons/prêts entre joueurs via un hub).

---

## 4. Immobilier

- **7 quartiers** depuis la 1.0 : Garment District, Hell's Kitchen, Murray Hill, Midtown, Lower Manhattan, **Industry City** (zone industrielle/usines) et **The Hamptons** (résidentiel chic + retail strip, nouveau en 1.0) ([wiki Neighborhoods](https://big-ambitions.fandom.com/wiki/Neighborhoods), [PCGamesN](https://www.pcgamesn.com/big-ambitions/1-0-tycoon-life-sim)). Chaque quartier a ses **demandes de marché, données démographiques, prix immobiliers et rivaux** propres (sauf Lower Manhattan, sans rival).
- Ordre de grandeur : le Garment District seul compte **165 bâtiments** ; prix moyens au m² distincts retail/bureau/résidentiel (ex. 7 848 $/m² retail, 20 250 $/m² résidentiel) ([wiki Garment District](https://big-ambitions.fandom.com/wiki/Garment_District)).
- **Location ET achat** d'adresses : appartements (résidence, meublables), locaux commerciaux, bureaux, **entrepôts** ; on peut acheter des immeubles entiers (objectif d'oncle Fred). Chaque bâtiment a un **Traffic Index fixe** (non modifiable) et une **capacité clients/heure (4 à 75)** selon le type ([wiki Buildings](https://big-ambitions.fandom.com/wiki/Buildings)).
- **Interior Designer** : outil d'aménagement intégré — placement de meubles, murs/sols (upgrade ≥ 5 000 $), files d'attente générées automatiquement par caisse et ajustables ([discussions](https://steamcommunity.com/app/1331550/discussions/0/3825284144450819116/)). Meubles achetés physiquement chez **IKA BOHAG** (déco), **AJ Pederson & Son** et **The Appliance Store / Square Appliances** (fonctionnel).
- **Blueprints** : plans d'aménagement sauvegardables, partageables via **Steam Workshop**, installés par des **interior installation firms** contre paiement (coût élevé, ex. 275 000 $) ; un blueprint ne s'applique qu'à un bâtiment de même gabarit (A1, A2…) ([aide blueprints](https://www.biggerambitions.com/help-blueprints.php)).

**État porté** : registre des baux/titres de propriété par adresse, loyers, contenu meublé de chaque intérieur (positions, types, état des objets), files d'attente.
**Fréquence** : faible (transactions rares) mais les aménagements sont des rafales d'édition d'objets.
**Difficulté** : moyenne-haute. Les transactions sont des événements discrets faciles à répliquer, avec **verrouillage côté hôte pour éviter la double location de la même adresse** (conflit classique). L'aménagement intérieur = réplication d'un placement d'objets (id, position, rotation) + snapshot complet au join.

---

## 5. Entreprises

- **Types** : une trentaine au total. Retail : gift shop, supermarché, coffee shop, fast-food, fruits & légumes, fleuriste, coiffeur, librairie, liquor store, boîte de nuit, bijouterie, électronique, vêtements, gym, **cinéma et théâtre** (EA 0.10)… ; **bureaux « digitaux »** : cabinet d'avocats, agence de développement web (+2 types ajoutés en 0.11), dont les clients sont virtuels (jamais dans le bâtiment) ; plus **entrepôts, usines, HQ** ([wiki Business Types](https://big-ambitions.fandom.com/wiki/Business_Types), [Law Firm](https://big-ambitions.fandom.com/wiki/Law_Firm), [Web Development Agency](https://big-ambitions.fandom.com/wiki/Web_Development_Agency)).
- **Registre** : création via BizMan (nom, **logo personnalisable** — fichiers dans un dossier `CustomIcons`) ([aide custom icon](https://www.biggerambitions.com/help-custom-icon.php)).
- **Produits & prix** : chaque produit a un prix d'achat en gros et un prix de vente réglable ; le **markup toléré dépend du quartier** (≈10 % en quartier pauvre, jusqu'à ≈150 %+ à Midtown) ([discussion pricing](https://steamcommunity.com/app/1331550/discussions/0/596268535200660216/)). La 1.0 ajoute le rôle **Pricing Manager** au HQ pour repricer tout un quartier d'un coup.
- **Flux de clients (simulation)** : Promotion = **Traffic Index (fixe, par bâtiment) + Marketing** (2 % de marketing = 1 % de promotion ; pour les bureaux P = T + M/2). 100 % de promotion = potentiel client max, plafonné par la **capacité du bâtiment (4–75 clients/h)**, modulé par **demande du quartier, type/taille de magasin, concurrents, satisfaction client, multiplicateurs horaires et journaliers** ; les clients sont des PNJ instanciés qui entrent, prennent des articles et passent en caisse (files d'attente) ([wiki Marketing](https://big-ambitions.fandom.com/wiki/Marketing), [discussion](https://steamcommunity.com/app/1331550/discussions/0/4358998952355015691/), [guide multipliers](https://steamcommunity.com/sharedfiles/filedetails/?id=3695018728)).
- **Heures d'ouverture** : librement planifiables (24/7 possible), fort impact sur la rentabilité selon le type (nightclub la nuit, bureaux fermés le week-end).
- **Rivaux** : ~20 propriétaires IA (app Rivals), qui possèdent commerces et immeubles ; ouvrir sur leur « territoire » déclenche des attaques (manipulation prix/demande, débauchage d'employés) ; on peut **racheter leurs commerces** (tout d'un bloc) ([wiki Rivals](https://big-ambitions.fandom.com/wiki/Rivals)).
- Statistiques par magasin (Insights BizMan : clients/heure, CA, etc.).

**État porté** : par business — registre, produits+prix, stocks en rayon/réserve, marketing actif, horaires, caisse (cash), stats ; plus l'état transitoire des PNJ clients ; état des rivaux.
**Fréquence** : la simulation clients tourne en continu pendant les heures d'ouverture (le plus gros volume d'état du jeu avec la circulation).
**Difficulté : ÉLEVÉE** pour les clients (simulation continue non déterministe) — à faire tourner **uniquement chez l'hôte**, les clients étant purement cosmétiques chez les invités (ou même non répliqués en v1, seuls les résultats — ventes, stocks, cash — comptent). Les réglages (prix, horaires, marketing) sont des événements discrets faciles.

---

## 6. Employés

- **Recrutement** : agences physiques/téléphonables — Anderson Recruitment Corp. (livreurs, etc.), **City Workforce Inc.** pour les rôles HQ ([wiki Employees](https://big-ambitions.fandom.com/wiki/Employees)).
- **Rôles** : customer service (retail), caissier, nettoyage, **delivery driver** (skill ≥75 % requis pour conduire le Freight truck), avocat, programmeur, ouvriers d'usine, et au HQ : **Logistics Manager** (1 par entrepôt, nombre de magasins gérés selon skill), **Purchasing Agent** (commande auprès des grossistes/importateurs, négocie les prix selon skill), **HR Manager** (formation passive, remplacement des malades, assurance santé), **Pricing Manager** (1.0). Les rôles HQ exigent bureau + poste informatique.
- **Skill en %** : détermine efficacité, véhicules autorisés, capacité de gestion ; **pas de progression automatique** — formation manuelle (l'employé quitte le planning) ou passive via HR (jusqu'à 50 % par défaut, 100 % possible, en continuant de travailler) ([discussion training](https://steamcommunity.com/app/1331550/discussions/0/3827536762642869444/)).
- **Satisfaction** : jusqu'à **3 demandes par employé** (Critique / Importante / Confort) — machine à café, chaise précise, horaires de jour, semaine de 5 jours… ; insatisfaction → menace de démission ; surmenage → **maladie** (HR couvre les absences).
- **Planning** : par jour et par shift, multi-shifts, heures max hebdo ; salaires horaires (payés sur la semaine).

**État porté** : liste d'employés (identité, rôle, skill, salaire, demandes, satisfaction, affectation, planning), état runtime (présent/malade/en formation).
**Fréquence** : lente (évolutions par heures/jours de jeu) + événements RH ponctuels.
**Difficulté** : moyenne. Simulation côté hôte ; les embauches/affectations/plannings sont des événements discrets. Piège : le **pool de candidats** des agences doit être partagé (deux joueurs ne doivent pas embaucher le même candidat) et le débauchage par les rivaux touche tout le monde.

---

## 7. Logistique

- **Grossistes (Wholesalers)** : approvisionnement de départ, achat sur place (drive-in) ou **contrats de livraison récurrents** ; commande urgente livrée le lendemain matin (+20 %) ([wiki Wholesalers](https://big-ambitions.fandom.com/wiki/Wholesalers)).
- **Importateurs** : gros volumes, situés sur les docks au sud de la carte, spécialisés (Lunar Tide = alimentaire ; Maritime Freight Line = électronique/bijoux ; Aquatic Bay = alcools) ; livraisons à minuit au dépôt/à l'usine ([wiki Importers](https://big-ambitions.fandom.com/wiki/Importers), [gamepressure import](https://www.gamepressure.com/newsroom/import-in-big-ambitions-list-of-importers-and-goods-with-prices/zd52a5)).
- **Entrepôts** : nécessitent Logistics Manager (HQ) + Delivery Driver + véhicule garé à l'entrepôt ; **une tournée par jour à 2 h du matin**, Freight truck = 4 magasins (jusqu'à 8 avec un manager à 100 %) ([wiki Warehouse](https://big-ambitions.fandom.com/wiki/Warehouse)).
- **Usines (Factories)** : dans des bâtiments d'entrepôt, stations de production par famille de produits (bijoux, électronique, agroalimentaire, bière, tabac, vêtements…) ; chaîne imposée : **matières premières importées → usine → produits finis → entrepôt → magasins** (pas de liaison directe usine→magasin) ; besoins : ouvriers, casiers, alimentation électrique, zone de réception ([wiki Factory](https://big-ambitions.fandom.com/wiki/Factory), [guide factories](https://big-ambitions.wiki/guides/factories/)).
- Manutention physique : storage shelf (16 cartons), hand truck (4), flatbed (8), coffres.

**État porté** : contrats fournisseurs, stocks entrepôt/usine, files de production, plans de tournées, véhicules d'entreprise.
**Fréquence** : ticks planifiés (minuit, 2 h) + production continue en heures ouvrées.
**Difficulté** : moyenne-haute mais **entièrement simulable côté hôte** (aucune interaction temps réel requise) ; les invités n'ont besoin que des résultats (stocks). Les événements de 2 h/minuit tombent pendant la « nuit skippée » → bien rejouer ces ticks lors d'un skip voté.

---

## 8. Progression

- **Story mode = oncle Fred** : tutoriel et fil d'objectifs qui court jusqu'au très long terme — 2 M$ en banque, 1 M$ investi, 100 % bonheur, immeuble acheté, 50 M$ puis 100 M$ de valeur nette, **80 businesses**, atteindre **65 ans**… ([wiki Objectives](https://big-ambitions.fandom.com/wiki/Objectives), [guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3502526963)).
- **Custom mode** : sans tutoriel ni objectifs, avec **sliders** (salaires, coût des marchandises, taxes…) ([discussion](https://steamcommunity.com/app/1331550/discussions/0/3790380616231788855/)).
- **Difficultés** : Easy 15 000 $ de départ / marché à 70 % / 10 % de taxes ; Normal 10 000 $ ; Hard 4 500 $ / marché 100 % / frais bancaires 5 % ([discussion](https://steamcommunity.com/app/1331550/discussions/0/3827536762635570932/)).
- Achievements Steam (casino, parking tickets, etc.). Pas de « prestige » ; l'endgame = croissance + rachat des rivaux + objectifs tardifs.

**État porté** : index de progression des objectifs, drapeaux de tutoriel, réglages de partie.
**Fréquence** : très faible.
**Difficulté** : faible techniquement, mais **choix de design** : en coop, la story d'oncle Fred est mono-joueur — recommandation v1 : parties **custom mode** uniquement (c'est aussi ce que fait le paysage moddé), objectifs par joueur non synchronisés.

---

## 9. Ville / monde

- New York stylisé, **7 quartiers**, monde ouvert continu (pas de chargements entre quartiers).
- **PNJ piétons** (s'arrêtent aux passages), **trafic routier IA** (comportements par type de véhicule), taxis errants, métro ([wiki Development](https://big-ambitions.fandom.com/wiki/Development)).
- **Magasins/lieux tiers où le joueur consomme** : IKA BOHAG (meubles déco, clone d'IKEA), AJ Pederson & Son (mobilier pro), The Appliance Store/Square Appliances, concessions auto, 2 stations essence + ateliers de réparation, grossistes, importateurs, 2 banques, IRS, agences de recrutement, installation firms, Onyx Luxury Chauffeurs (1.0), Speedy Bites (1.0), **Casino Boat** (vendredi), restaurants/fast-foods pour les besoins.
- **Événements « aléatoires »** : tickets de parking (150 $/3 h), pannes/dégâts véhicule + dépanneuse, maladie d'employés, attaques de rivaux. Pas (encore) de vols/cambriolages ni de police — souvent demandés sur le forum, non implémentés ([forum](https://forum.bigambitionsgame.com/t/theft-robberies/301)).
- Minijeux 1.0 (Brick Breaker sur le PC du domicile).

**État porté** : quasi rien de persistant (piétons/trafic décoratifs, régénérés) ; seuls comptent les événements à conséquence (ticket, panne).
**Fréquence** : continue mais cosmétique.
**Difficulté** : faible si l'on accepte que **le décor ne soit pas identique chez chacun** (piétons/voitures locaux, non synchronisés en v1 — même choix que Going Public qui ne réplique que les joueurs).

---

## 10. Sauvegarde / méta

- **Format : JSON** (éditable — les guides de cheat modifient `"money"` directement) ([VGTimes](https://vgtimes.com/games/big-ambitions/files/cheats/)). Emplacement : `%USERPROFILE%/AppData/LocalLow/Big Ambitions/SaveGames` + Steam Cloud ([DigiStatement](https://digistatement.com/big-ambitions-save-file-location-where-is-it/), [PCGamingWiki](https://www.pcgamingwiki.com/wiki/Big_Ambitions)).
- **Autosave : toutes les 5 minutes, 3 sauvegardes de récupération par défaut** — fréquence et nombre configurables dans les options ([discussion](https://steamcommunity.com/app/1331550/discussions/0/5796774642236900897/)). Slots de sauvegarde manuels multiples + save on quit.
- **Prix/éditions** : base 22,99 $ ; **DLC Silver et Gold à 9,99 $** chacun (packs supporter/cosmétiques) ([SteamDB DLC](https://steamdb.info/app/1331550/dlc/)).
- **Techno & modding** : Unity compilé **IL2CPP** ; la scène modding utilise **MelonLoader** (+ pont BepInEx) et Harmony ([MelonLoader](https://melonloader.net/modding-big-ambitions-with-melonloader/), [BepInEx.MelonLoader.Loader](https://github.com/BepInEx/BepInEx.MelonLoader.Loader/)). Depuis **EA 0.11**, **Steam Workshop officiel** (mods activables au menu principal + blueprints) ([Steam news 0.11](https://store.steampowered.com/news/app/1331550/view/679623809418921531)). Position officielle : modding « non supporté », risque de casse à chaque patch. Frameworks communautaires existants : [BAUI-Framework](https://www.nexusmods.com/bigambitions/mods/6) (UI), AutoIdle.

**Implications coop** : le JSON de save est une **aubaine** — il documente le modèle d'état complet et fournit un mécanisme naturel de **snapshot au join** (l'hôte sérialise, le client charge). L'IL2CPP est le principal **piège technique** (noms mutilés, unhollowing à refaire à chaque mise à jour du jeu).

---

## 11. Mises à jour (historique récent et à venir)

| Version | Date | Contenu clé |
|---|---|---|
| EA launch | mars 2023 | Sortie Early Access ([SteamDB](https://steamdb.info/app/1331550/patchnotes/)) |
| EA 0.4 | fin 2023 | QoL, nettoyage de code, Electronics Store ([X Hovgaard](https://x.com/hovgaardgames/status/1729882023366631564)) |
| EA 0.5 | 2024 | **Rivals** (concurrents IA) |
| EA 0.6 | oct. 2024 | Refonte UI/overlay, équilibrage |
| EA 0.10 | ~2025-2026 | « No Business Like Show Business » : Broadway, **Cinéma & Théâtre**, **refonte des usines**, grosses optimisations ([YouTube](https://www.youtube.com/watch?v=wmj8_yDECTU)) |
| EA 0.11 | 2026 | « The Workshop Awakens » : **Steam Workshop / support officiel des mods**, 2 business de bureau, energy drinks ([Steam news](https://store.steampowered.com/news/app/1331550/view/679623809418921531)) |
| **1.0** | **28 août 2026** | **The Hamptons** (7ᵉ quartier), chauffeur privé, livraison de repas, **HQ Pricing Manager**, refonte banking/impôts, minijeux ([Steam news 1.0](https://store.steampowered.com/news/app/1331550/view/716786116904093572), [PCGamesN](https://www.pcgamesn.com/big-ambitions/1-0-tycoon-life-sim)) |
| Post-1.0 | mois à venir | « Quelques updates » puis **premier DLC payant** ([Notebookcheck](https://www.notebookcheck.net/Highly-rated-business-sim-finally-leaves-Early-Access-and-gets-20-Steam-discount.1382596.0.html)) — **risque de casse répété du mod** (IL2CPP re-mutilé à chaque build) |

**Multijoueur officiel : non prévu** dans cette version du jeu (« maybe » de longue date, jamais promis) ([discussion + réponse dev](https://steamcommunity.com/app/1331550/discussions/0/612031852355541814/)). Un précédent existe : le mod Workshop **[Going Public: Multiplayer for Big Ambitions](https://steamcommunity.com/sharedfiles/filedetails/?id=3765662670)** — ville partagée, horloge commune, chaque joueur a son argent/staff/bâtiments, les autres joueurs apparaissent comme rivaux, chat, prêts/dons, passager en voiture, **skip de nuit par vote**, invitations Steam/IP directe/LAN, réglages hôte pour le drain des besoins. À étudier de près (fonctionnalités = validation de notre découpage ; ses limites = nos opportunités, p. ex. vraie **coop d'une même entreprise**, que Going Public ne fait pas).

---

## Matrice de synchronisation

Modèle retenu : **hôte autoritaire** (l'hôte fait tourner toute la simulation économique), clients = présence + intentions ; **snapshot au join** dérivé de la sérialisation JSON de save ; horloge unique avec skip coopératif.

| Système | État porté | Fréquence de changement | Stratégie de synchro proposée | Phase | Risque |
|---|---|---|---|---|---|
| Horloge / calendrier | date, heure, vitesse | continue (1 s réelle = 1 min jeu) | **hôte seul** fait autorité ; tick diffusé + correction de dérive ; **skip (sommeil/caisse) par vote unanime** | **1** | **Élevé** — tout en dépend ; ticks planifiés (2 h, minuit, lundi) à rejouer lors des skips |
| Position/anim joueurs, véhicule conduit | transform, état véhicule | continue (10-20 Hz) | événement répliqué (interpolation client) | **1** | Moyen — netcode classique ; véhicules = physique locale + réconciliation |
| Besoins du perso (énergie/faim/bonheur) | 3 jauges par joueur | continue lente | **local au joueur**, jamais simulé par l'hôte ; drain indexé sur l'horloge commune | 1 | Faible |
| Argent perso / banque / prêts / IRS | soldes, prêts, dette fiscale | transactionnel + échéances | par joueur, **transactions validées par l'hôte** ; échéances déclenchées par l'horloge hôte | 1-2 | Moyen — double-dépense/duplication à verrouiller |
| Immobilier (location/achat) | registre adresses→propriétaire | rare | **événement répliqué avec arbitrage hôte** (premier arrivé) ; snapshot au join | **2** | Moyen — conflits d'achat simultané |
| Aménagement intérieur (Interior Designer, meubles) | liste d'objets placés par intérieur | rafales lors de l'édition | événement répliqué (place/retire/déplace) + snapshot au join ; verrou d'édition par pièce en option | 2 | Moyen — volume d'objets ; blueprints = transaction unique |
| Réglages business (prix, horaires, marketing, logo, registre) | config par business | rare | événement répliqué (petits messages) | **2** | Faible |
| Stocks & caisse des magasins | inventaires rayons/réserve, cash | continu en heures d'ouverture | **hôte seul** simule ; deltas périodiques + à l'entrée du joueur dans le bâtiment | 2 | Moyen |
| Clients PNJ en magasin | agents transitoires | très haute | **hôte simule les résultats** ; représentation visuelle locale approximative chez les invités (voire **pas synchronisé en v1** hors du bâtiment où se trouve un joueur) | 3 | **Élevé** — ne jamais tenter le lockstep dessus |
| Employés (embauche, planning, satisfaction, formation, maladie) | roster complet | lent + événements | **hôte seul** simule ; actions RH = événements répliqués ; **pool de candidats partagé arbitré par l'hôte** | 2-3 | Moyen |
| Logistique (grossistes, imports, entrepôts, tournées 2 h, usines) | contrats, stocks, files de production | ticks planifiés + continu | **hôte seul** ; résultats poussés en delta ; rejouer les ticks pendant les skips | **3** | Moyen-élevé — cohérence des ticks nocturnes avec le skip voté |
| Rivals IA | état des 20 rivaux, attaques, buyouts | lent | **hôte seul** ; événements notifiés à tous | 3 | Faible-moyen |
| Manutention physique (cartons, hand truck, coffres) | objets portés/en transit | interaction ponctuelle | événement répliqué avec propriété d'objet (ownership) arbitrée hôte | 3 | Moyen — duplication d'items si mal verrouillé |
| Trafic routier / piétons décoratifs | agents cosmétiques | continue | **pas synchronisé en v1** (simulation locale par client) | 4 (jamais ?) | Faible — incohérence visuelle acceptable |
| Taxis / métro / chauffeur privé / Speedy Bites | trajets du joueur | ponctuel | local au joueur + événement de téléportation répliqué | 2 | Faible |
| Progression oncle Fred / objectifs | index d'objectifs | très rare | **pas synchronisé en v1** (coop = custom mode ; objectifs par joueur sinon) | 4 | Faible (choix de design) |
| Casino / minijeux | session de jeu | ponctuel | local au joueur ; seul le résultat financier passe par l'hôte | 4 | Faible |
| Sauvegarde de partie | JSON complet du monde | à la demande / 5 min | **hôte seul** sauvegarde ; ce JSON sert de **snapshot au join** ; état perso des invités stocké dans un sidecar | **1** (infra) | **Élevé** — format non documenté, change à chaque grosse MAJ |
| Difficulté / réglages de partie | sliders custom | à la création | fixés par l'hôte à la création, diffusés au join | 1 | Faible |

### Phasage recommandé
- **Phase 1 — Fondation** : transport réseau (Steam/IP/LAN), horloge hôte + vote de skip, présence des joueurs (position/anim/véhicules), snapshot au join via la save JSON, argent par joueur.
- **Phase 2 — Monde partagé** : immobilier + verrous, réglages business, stocks/caisse en delta, aménagement intérieur, embauches simples, transports personnels.
- **Phase 3 — Simulation profonde** : logistique complète et ticks nocturnes, usines, RH avancée, rivaux, manutention partagée, clients visibles en co-présence.
- **Phase 4 — Confort** : chat/échanges entre joueurs, passager en voiture, blueprints partagés, éventuelle co-propriété d'une même entreprise (le vrai différenciateur vs Going Public), cohérence décorative.

### Pièges transverses (à garder sous les yeux)
1. **IL2CPP** : pas de C# lisible ; MelonLoader/Harmony obligatoires, régénération des proxies à **chaque patch** — et Hovgaard annonce plusieurs updates + un DLC dans les mois qui viennent. Prévoir un pinning de version du jeu + CI de re-unhollowing.
2. **Tout le gameplay est indexé sur l'horloge** : chaque désynchronisation de temps casse livraisons (2 h), paie, frais du lundi, IRS (60 j), casino (vendredi). L'horloge doit être le premier et le plus solide des systèmes.
3. **Le skip de temps est le mode de jeu normal** (dormir 8-24 h, travailler à la caisse) : sans skip coopératif fluide, le coop est injouable — le vote unanime de Going Public est le standard de facto.
4. **Save JSON non documentée et mouvante** : baser le snapshot au join dessus est le chemin le plus court, mais chaque MAJ du schéma est une casse potentielle.
5. **Position officielle** : pas de multi prévu, modding « non supporté » — aucune aide API à attendre, mais le Workshop officiel (0.11) donne un canal de distribution propre.
