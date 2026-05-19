# 🚀 AstroVoxel

**AstroVoxel** est un jeu de survie/exploration spatiale en voxels développé avec Unity. Inspiré de Minecraft, il transpose l'expérience de construction et d'exploration dans un univers entièrement procédural : planètes sphériques explorables, ceintures d'astéroïdes en orbite, vaisseau spatial, cycle jour/nuit, atmosphère dynamique et sauvegarde JSON.

---

## 📋 Table des matières

- [Aperçu du projet](#-aperçu-du-projet)
- [Architecture technique](#-architecture-technique)
- [Moteur voxel sphérique](#-moteur-voxel-sphérique-18-face-cube-sphère)
- [La planète de base](#-la-planète-de-base-home-planet)
- [Planètes procédurales infinies](#-planètes-procédurales-infinies)
- [Les biomes planétaires](#-les-biomes-planétaires)
- [Ceinture d'astéroïdes](#-ceinture-dastéroïdes)
- [Météorites](#-météorites)
- [Le joueur](#-le-joueur)
- [Le vaisseau spatial](#-le-vaisseau-spatial)
- [Types de blocs](#-types-de-blocs-108-types)
- [Système de craft](#-système-de-craft)
- [Environnement et atmosphère](#-environnement-et-atmosphère)
- [Shaders personnalisés](#-shaders-personnalisés)
- [Système de sauvegarde](#-système-de-sauvegarde)
- [Console de commandes](#-console-de-commandes)
- [Modes de jeu](#-modes-de-jeu)
- [Structure du projet](#-structure-du-projet)
- [Assets tiers](#-assets-tiers)
- [Feuille de route](#-feuille-de-route)

---

## 🌌 Aperçu du projet

AstroVoxel est un sandbox spatial dans lequel le joueur :

- **Atterrit sur une planète procédurale** avec herbe, grottes, arbres et minerais
- **Explore un système solaire** comprenant 512 planètes procédurales uniques
- **Traverse une ceinture d'astéroïdes** en orbite, chacun minable en voxels
- **Pilote un vaisseau 6DOF** pour voyager dans le système solaire
- **Construit et survit** avec un système de craft style Minecraft
- **Sauvegarde et charge** ses mondes via une console de commandes

### Stack technologique

| Technologie | Version |
|---|---|
| **Moteur** | Unity (URP) |
| **Langage** | C# (.NET) |
| **Pipeline de rendu** | Universal Render Pipeline (URP) |
| **Input System** | Unity New Input System (rétrocompatible Old Input) |
| **Sérialisation** | JSON (System.Text / JsonUtility) |
| **Réseau** | Netcode for GameObjects (préparation multijoueur) |

---

## 🏗️ Architecture technique

Le projet suit une architecture **code-first** : toute la scène est générée par code depuis un unique MonoBehaviour `GameBootstrap` attaché à un GameObject vide. Aucun prefab n'est requis dans la scène.

```
GameBootstrap.Awake()
├── WorldSeedManager.Initialize()      — seed globale déterministe
├── BuildEnvironment()                 — skybox spatiale
├── BuildSun()                         — soleil orbital (cycle jour/nuit)
├── BuildPlanet()                      — planète de base (PlanetWorld)
├── BuildPlayer()                      — joueur (PlayerController + GravityBody)
├── BuildAtmosphere()                  — atmosphère dynamique
├── BuildSpaceShip()                   — vaisseau spatial 6DOF
├── BuildAsteroidSystem()              — ceinture d'astéroïdes + météorites
├── BuildInfinitePlanets()             — système de 512 planètes
└── BuildSaveSystem()                  — gestionnaire de sauvegardes
```

### Namespaces C#

| Namespace | Rôle |
|---|---|
| `AstroVoxel.Bootstrap` | Point d'entrée de la scène |
| `AstroVoxel.VoxelEngine` | Moteur voxel, génération de terrain, types de blocs |
| `AstroVoxel.Space` | Planètes infinies, astéroïdes, météorites, seed |
| `AstroVoxel.Player` | Contrôleur joueur, inventaire, craft, HUD, console |
| `AstroVoxel.Physics` | Gravité sphérique (GravityAttractor, GravityBody) |
| `AstroVoxel.Environment` | Atmosphère, skybox, soleil |
| `AstroVoxel.Vehicle` | Vaisseau spatial |
| `AstroVoxel.Save` | Sérialisation/désérialisation des sauvegardes |

---

## 🌍 Moteur voxel sphérique (18-Face Cube-Sphère)

Le moteur voxel est la pièce centrale du projet. Il résout le problème classique de l'effet escalier (staircase artifact) lors de la projection d'une grille cubique sur une sphère.

### Principe : Cube-Sphère Octaédrique 18 faces

La planète est subdivisée en **18 faces** (6 faces d'un cube × 3 niveaux d'orientation) au lieu des 6 habituelles. Chaque chunk est orienté **radialement** par rapport au centre planétaire via une rotation `Quaternion`, ce qui garantit que :

- Les blocs sont parfaitement alignés avec la surface (pas d'escalier)
- Les faces des blocs sont toujours perpendiculaires au sol local
- La gravité s'applique naturellement de manière radiale

### Clé de chunk : `FaceChunkCoord`

```
FaceChunkCoord(face, U, V, R)
  face — FaceIndex parmi 18 orientations
  U    — colonne dans l'espace face-local (axe Rights)
  V    — rangée dans l'espace face-local (axe Forwards)
  R    — couche radiale (axe Normals, vers le centre)
```

### Dimensions

| Paramètre | Valeur |
|---|---|
| Taille d'un chunk | 16 × 16 × 16 blocs |
| Volume d'un chunk | 4 096 blocs |
| Atlas de textures | 4 × 4 tuiles |

### Algorithme de couverture complète

L'algorithme de spawn de chunks garantit mathématiquement qu'aucun bloc de la surface n'est manqué :

- Pas d'échantillonnage = `cs/2 = 8` blocs
- Rayon inscrit d'un chunk = `cs/2 = 8`
- Distance max d'un point à son voisin de grille : `h·√3/2 ≈ 6,93 < 8`
- **Résultat** : couverture complète garantie, ~2 000 chunks par planète

### Chargement asynchrone

Les planètes distantes se chargent via une coroutine (`LoadChunksAsync`) qui instancie **6 chunks par frame** pour éviter les freezes. La home planet se charge synchroniquement avant le premier `FixedUpdate`.

---

## 🌱 La planète de base (Home Planet)

La planète de base est une planète **Terran** générée de manière procédurale à chaque nouvelle partie selon la seed mondiale.

### Paramètres de génération

| Paramètre | Valeur |
|---|---|
| Rayon du noyau | 50 blocs |
| Épaisseur de croûte | 12 blocs |
| Amplitude de surface | ±3 blocs |
| Fréquence de surface | 0,03 |
| Rayon de la planète (Unity units) | 165 u |

### Couches de terrain (du centre vers l'extérieur)

```
Distance < surfaceRadius - 3,5 blocs → Pierre (Stone)
Distance < surfaceRadius - 0,5 blocs → Terre (Dirt)
Distance ≈ surfaceRadius             → Herbe (Grass)
Distance > surfaceRadius             → Air
```

### Système de grottes

Le système de grottes utilise un bruit de Perlin worm multi-fréquence :

| Paramètre | Valeur | Description |
|---|---|---|
| `CaveFrequency` | 0,045 | Fréquence spatiale des tunnels |
| `CaveTubeRadius` | 0,110 | Épaisseur en profondeur (~5 blocs) |
| `CaveEntryRadius` | 0,068 | Épaisseur en surface (~3 blocs) |
| `CavePresenceThreshold` | 0,70 | ~15% de la planète a des grottes |
| `CaveTransitionDepth` | 8 blocs | Transition surface → profond |

Les tunnels débouchent naturellement en surface et respectent la limite de croûte (`CrustThickness + 3 blocs`).

### Génération des arbres

Les arbres sont générés procéduralement à la surface selon la densité du biome. Chaque espèce utilise son type de tronc et feuillage correspondant (Chêne, Épicéa, Bouleau, Jungle, Acacia, DarkOak, Mangrove, Cerisier).

---

## 🪐 Planètes procédurales infinies

Le `InfinitePlanetSystem` gère **512 planètes** dans le système solaire, positionnées de manière déterministe à partir de la seed mondiale.

### Système LOD à trois niveaux

```
Distance > 80 000 u   → non affiché
Distance < 80 000 u   → Impostor (sphère colorée, zéro GameObject)
Distance < rayon + 600 u → Planète voxel complète (chargement async)
```

**Impostor** : rendu via `Graphics.DrawMesh()` (zéro allocation GC, zéro GameObject). La couleur de l'impostor dépend du biome.

### Hystérésis de chargement

| Seuil | Valeur | Rôle |
|---|---|---|
| `VoxelActivateBuffer` | +600 u au-dessus du rayon | Déclenchement du chargement voxel |
| `VoxelDeactivateBuffer` | +900 u au-dessus du rayon | Déchargement des voxels |

### Paramètres de génération par planète

Chaque planète reçoit une `PlanetGenerationConfig` déterministe :

- **Rayon** : variable (small / medium / large)
- **Biome** : choisi pseudo-aléatoirement
- **NoiseOffset** : dérivé de la seed pour que deux planètes identiques aient des terrains différents
- **Grottes, arbres** : activés/désactivés selon le biome

---

## 🌈 Les biomes planétaires

11 biomes distincts, chacun avec ses blocs, son amplitude de terrain et ses règles de génération :

| Biome | Couleur impostor | Blocs principaux | Amplitude | Grottes | Arbres |
|---|---|---|---|---|---|
| **Terran** 🟢 | Vert | Herbe / Terre / Pierre | 6% du rayon | Oui | Oui |
| **Desert** 🟡 | Sable | Sable / Grès / RedSand | 14% du rayon | Non | Rares (cactus) |
| **Snow** ⬜ | Blanc-bleu | PackedIce / Pierre / BlueIce | 7% du rayon | Oui | Rares (épicéas) |
| **Volcanic** 🔴 | Rouge sombre | Netherrack / Blackstone / Obsidian | 16% du rayon | Larges | Rares (obsidienne) |
| **Forest** 🌲 | Vert foncé | Jungle / Feuilles de Jungle | 5% du rayon | Oui | Dense |
| **Mountain** ⛰️ | Gris | Granite / Andesite / Deepslate | 22% du rayon | Oui | Rares |
| **Endstone** 🟫 | Beige | EndStone / PurpurBlock | 4% du rayon | Non | Non |
| **Crystal** 💜 | Violet | QuartzBricks / Améthyste / Deepslate | 5% du rayon | Oui | Rares (piliers) |
| **Nether** 🔥 | Rouge-brun | SoulSand / Netherrack / Glowstone | 17% du rayon | Larges | Champignons géants |
| **Cherry** 🌸 | Rose | CherryLog / CherryLeaves / Herbe | 7% du rayon | Oui | Normal |
| **Mossy** 🍀 | Vert mousse | MossyCobblestone / DarkOak | 8% du rayon | Oui | Dense |

---

## ☄️ Ceinture d'astéroïdes

Le `AsteroidField` génère procéduralement un anneau de **80 astéroïdes** en orbite autour de la planète de base.

### Paramètres orbitaux

| Paramètre | Valeur |
|---|---|
| Rayon intérieur | 120 u |
| Rayon extérieur | 200 u |
| Nombre d'astéroïdes | 80 |
| Taille (coreRadius) | 5 à 22 blocs |
| Vitesse orbitale | 0,03 à 0,09 rad/s |
| Inclinaison | ±20° |

### Architecture des astéroïdes

Chaque astéroïde est un GameObject composé de :

| Composant | Rôle |
|---|---|
| `AsteroidOrbit` | Mouvement orbital + rotation propre (tumble 3 axes) |
| `GravityAttractor` | Micro-gravité surfacique (portée = rayon × 2,5) |
| `AsteroidWorld` | Monde voxel cubique (grille axis-aligned, pas de cube-sphère) |
| `AsteroidLOD` | Gestion LOD automatique selon distance |

### Différence avec le moteur planétaire

Les astéroïdes utilisent une **grille cubique simple** (axis-aligned) au lieu du 18-face cube-sphère. Raison : les astéroïdes sont trop petits pour que l'effet escalier soit visible, et la grille sphérique causait du z-fighting et de la géométrie redondante × 6 au centre.

### Système LOD des astéroïdes

```
Distance > 800 u    → Culled (aucun rendu)
300–800 u           → Mesh LOD (icosphère déformée ~300 triangles)
Distance < 300 u    → Voxels complets (AsteroidWorld chargé)
```

Le **mesh LOD** est une icosphère déformée par bruit de Perlin multi-octave, générée procéduralement (pas de modèle 3D externe). Pendant le chargement async des voxels, le mesh LOD reste visible pour éviter le "trou" visuel.

**Budget global** : `AsteroidSystemManager` limite le nombre d'astéroïdes en mode voxel simultanément. Si le budget est dépassé, le plus lointain est rétrogradé vers LODMesh.

### Gravité des astéroïdes

La force gravitationnelle est proportionnelle au volume (rayon³) :

```csharp
gravForce = Lerp(0.8f, 4.0f, (radius - minR) / (maxR - minR))
influence = radius × 2.5f
```

---

## 🌠 Météorites

Le `MeteoriteSpawner` gère un **pool de 8 météorites** préalloués, lancés périodiquement vers la planète.

| Paramètre | Valeur |
|---|---|
| Taille du pool | 8 météorites |
| Distance de spawn | 400 à 500 u |
| Vitesse | 80 à 200 u/s |
| Rayon | 0,4 à 2,0 blocs |
| Intervalle | 8 à 25 secondes |
| Déviation de visée | ±15° |

Les météorites impactent les planètes et astéroïdes, créant des cratères en détruisant des blocs via l'API `BreakBlock()`.

---

## 🧑‍🚀 Le joueur

### Mouvement (PlayerController)

Contrôles **ZQSD** (AZERTY) et **WASD** (QWERTY) :

| Action | Touche |
|---|---|
| Déplacement | Z/Q/S/D ou W/A/S/D |
| Sprint | Shift gauche |
| Accroupi | A (ou Ctrl) |
| Saut | Espace |
| Auto-saut | Automatique (1 bloc de hauteur) |

| Paramètre | Valeur |
|---|---|
| Vitesse de marche | 5,5 u/s |
| Vitesse de sprint | 9 u/s |
| Vitesse accroupi | 2,2 u/s |
| Force de saut | 5,82 m/s (équivalent Minecraft : 1,252 bloc) |
| Gravité | 13,54 m/s² |

### Physique sphérique (GravityBody + GravityAttractor)

Le joueur est attiré par le corps céleste le plus proche (planète ou astéroïde). Le `GravityBody` :

1. Détecte l'attracteur le plus puissant dans sa zone d'influence
2. Applique la force via `GravityAttractor.Attract()`
3. Aligne le "bas" du joueur vers le centre planétaire (`ForceMode.Acceleration`)

### Santé (PlayerHealth) — Mode Survie

| Paramètre | Valeur |
|---|---|
| HP maximum | 20 (10 cœurs) |
| Régénération | +1 HP toutes les 4 secondes |
| Hauteur min chute mortelle | 3 blocs |
| Dégâts de chute | 1 HP par bloc au-delà de 3 |

### Interaction avec les blocs (BlockInteraction)

- **Clic gauche** : casser un bloc (rayon de portée configurable)
- **Clic droit** : poser un bloc (type sélectionné dans la hotbar)
- **Wireframe** : aperçu en fil de fer du bloc ciblé (`BlockWireframe`)
- **Drop** : les blocs cassés génèrent un item au sol (`BlockDropController`)

### Inventaire

| Mode | Système |
|---|---|
| **Créatif** | `CreativeInventory` — tous les blocs disponibles |
| **Survie** | `SurvivalInventory` — gestion de stacks, drops, craft |

---

## 🚀 Le vaisseau spatial

### Contrôles 6DOF (SpaceShipController)

| Action | Touche |
|---|---|
| Poussée avant | W ou Z |
| Freinage / arrière | S |
| Dérive gauche/droite | A / D |
| Roulis gauche/droite | Q / E |
| Lacet | ← / → |
| Tangage | ↑ / ↓ |
| Poussée verticale | Espace / Ctrl gauche |
| Boost | Shift gauche (×3) |
| Embarquer/Débarquer | F |
| Lacet/Tangage souris | Mouvement souris |

### Paramètres de vol

| Paramètre | Valeur |
|---|---|
| Poussée principale | 35 m/s² |
| Poussée latérale | 25 m/s² |
| Vitesse max | 200 u/s |
| Boost | ×3 |
| Sensibilité souris | 0,12 |

### Physique de vol

Le vaisseau bascule automatiquement entre deux modes selon la position par rapport à la couche d'ozone (`OzoneLayer`) :

- **Mode spatial** : inertie pure, traînée nulle, liberté totale de rotation
- **Mode atmosphérique** : gravité + traînée aérodynamique

### Caméra vaisseau

La `SpaceShipCamera` offre une vue à la troisième personne avec bras de levier configurables pour suivre le vaisseau en vol.

---

## 🧱 Types de blocs (108 types)

AstroVoxel dispose de **108 types de blocs stockables** (IDs 1–108) plus des IDs de rendu internes (200–222).

### Blocs de base
`Stone`, `Dirt`, `Grass`, `Sand`, `Wood/OakLog`, `Leaves/OakLeaves`

### Pierre et variantes (13 types)
`Cobblestone`, `MossyCobblestone`, `Andesite`, `PolishedAndesite`, `Diorite`, `PolishedDiorite`, `Granite`, `PolishedGranite`, `StoneBricks`, `CrackedStoneBricks`, `MossyStoneBricks`, `ChiseledStoneBricks`, `SmoothStone`

### Terre et sol (6 types)
`Gravel`, `Clay`, `CoarseDirt`, `Podzol`, `Mud`, `MudBricks`

### Sable et grès (3 types)
`RedSand`, `Sandstone`, `RedSandstone`

### Bois — Planches (11 essences)
`OakPlanks`, `SprucePlanks`, `BirchPlanks`, `JunglePlanks`, `AcaciaPlanks`, `DarkOakPlanks`, `MangrovePlanks`, `CherryPlanks`, `CrimsonPlanks`, `WarpedPlanks`, `BambooPlanks`

### Bois — Troncs (7 essences)
`OakLog`, `SpruceLog`, `BirchLog`, `JungleLog`, `AcaciaLog`, `DarkOakLog`, `MangroveLog`... + `CherryLog`, `PaleOakLog`

### Feuillages (8 types)
`OakLeaves`, `SpruceLeaves`, `BirchLeaves`, `JungleLeaves`, `AcaciaLeaves`, `DarkOakLeaves`, `MangroveLeaves`, `CherryLeaves`

### Blocs spéciaux overworld
`Bedrock`, `Obsidian`, `Bricks`, `Ice`, `PackedIce`, `BlueIce`

### Nether (6 types)
`NetherBricks`, `RedNetherBricks`, `Netherrack`, `SoulSand`, `SoulSoil`, `Glowstone`

### End (4 types)
`EndStone`, `Blackstone`, `PurpurBlock`, `QuartzBricks`

### Deepslate (4 types)
`Deepslate`, `CobbledDeepslate`, `DeepSlateBricks`, `DeepSlateTiles`

### Minerais (8 types)
`CoalOre`, `IronOre`, `CopperOre`, `GoldOre`, `LapisOre`, `RedstoneOre`, `DiamondOre`, `EmeraldOre`

### Blocs de minerais compressés (10 types)
`CoalBlock`, `IronBlock`, `GoldBlock`, `DiamondBlock`, `EmeraldBlock`, `LapisBlock`, `RedstoneBlock`, `NetheriteBlock`, `CopperBlock`, `AmethystBlock`

### Blocs spéciaux biomes
`Cactus`, `MagmaBlock`, `MossBlock`, `MushroomStem`, `BrownMushroomBlock`, `RedMushroomBlock`, `Snow`, `Calcite`, `Tuff`, `Basalt`, `Dripstone`, `RootedDirt`, `NetherWartBlock`, `ShroomLight`, `PaleOakLog`, `PaleOakLeaves`

### Fonctionnel
`CraftingTable` — établi (déblocage des recettes 3×3)

---

## 🔨 Système de craft

Le `CraftingSystem` implémente des recettes style Minecraft :

- **Recettes 2×2** : disponibles dans l'inventaire de survie (sans établi)
- **Recettes 3×3** : nécessitent un `CraftingTable`

### Exemples de recettes

| Résultat | Ingrédients | Établi requis |
|---|---|---|
| 4 Planches de Chêne | 1 OakLog | Non |
| CraftingTable | 4 OakPlanks | Non |
| Épée de Bois | 2 OakPlanks + 1 Bâton | Oui |
| Coffre | 8 OakPlanks | Oui |
| Four | 8 Cobblestone | Oui |

---

## 🌤️ Environnement et atmosphère

### Atmosphère dynamique (AtmosphereRenderer)

L'atmosphère est rendue via deux sphères concentriques :

- **AtmosphereSky** : dégradé zénith (bleu foncé) → horizon (bleu clair)
- **OzoneRing** : anneau translucide de la couche d'ozone (transition espace/atmosphère)

Le rendu s'adapte en temps réel selon la position du joueur et l'heure du cycle solaire :

| État | Ambiance | Brouillard |
|---|---|---|
| Plein jour | Bleu-blanc doux | Ciel bleu clair |
| Coucher/lever | Ambre chaud | Orange-rose |
| Nuit | Quasi-noire (bleu) | Noir-bleu profond |
| Espace | Très sombre | Aucun |

### Cycle jour/nuit (SunOrbit)

Le soleil orbite autour de la planète. Le `SpaceSkyboxController` pilote la skybox spatiale (étoiles, nébuleuses) visible depuis l'espace.

### Physique OzoneLayer

La `OzoneLayer` détecte si le joueur/vaisseau est dans l'atmosphère ou dans l'espace, permettant de basculer les comportements physiques (traînée, gravité, brouillard).

---

## 🎨 Shaders personnalisés

Tous les shaders sont écrits pour **Universal Render Pipeline (URP)** :

| Shader | Rôle |
|---|---|
| `BlockVoxelUnlit` | Rendu des blocs voxel sans éclairage coûteux |
| `SpaceSkybox` | Skybox spatiale avec étoiles procédurales |
| `AtmosphereSky` | Dégradé atmosphérique zénith/horizon |
| `OzoneRing` | Couche d'ozone translucide |
| `PlanetImpostor` | Sphère colorée pour les planètes lointaines |
| `SunSurface` | Surface solaire animée |
| `SunCorona` | Couronne solaire lumineuse |
| `ThrusterParticle` | Particules de propulsion du vaisseau |

---

## 💾 Système de sauvegarde

### Format

Les sauvegardes sont stockées en **JSON** dans le dossier `Application.persistentDataPath` :

```
{AppDataPath}/AstroVoxel/saves/<nom>.json
```

### Contenu d'une sauvegarde

```json
{
  "worldSeed": 123456789,
  "playerPosition": { "x": 0, "y": 55, "z": 10 },
  "homePlanetMods": [ { "face": 0, "cu": 1, "cv": 2, "cr": 3, "lx": 5, "ly": 7, "lz": 2, "block": 3 } ],
  "infinitePlanetMods": { "1": [...], "42": [...] },
  "asteroidMods": { "0": [...] },
  "asteroidOrbitalAngles": { "0": 1.57, "1": 3.14 }
}
```

### Flux de chargement

```
/load <nom>
  → WorldSeedManager.ForceInitialize(seed)
  → PendingLoad = data
  → SceneManager.LoadScene()
  → GameBootstrap.Awake()
  → SaveSystem.Init()
  → ApplyPendingLoad()
    ├── home planet : mods appliquées immédiatement
    ├── planètes infinies : mods stockées, appliquées au chargement voxel
    ├── astéroïdes : angle orbital restauré + mods queued (après LOD load)
    └── joueur : position restaurée après 2 frames (physique stable)
```

---

## 💻 Console de commandes

La console s'ouvre avec **T** ou **/** et se ferme avec **Échap**.

### Commandes disponibles

| Commande | Description |
|---|---|
| `/save <nom>` | Sauvegarde l'état courant |
| `/load <nom>` | Charge une sauvegarde |
| `/saves` | Liste toutes les sauvegardes |
| `/restart` | Génère une nouvelle seed et recharge la scène |
| `/clear` | Vide la hotbar |
| `/help` | Affiche l'aide |

### Interface

L'interface de la console adopte un style terminal sombre (palette Apple Dark) :

```
┌─────────────────────────────────────────────────────┐
│  CONSOLE                        ESC — fermer        │
├─────────────────────────────────────────────────────┤
│  AstroVoxel Console — tapez help pour les commandes │
│  › /save monMonde                                   │
│  ✓  Monde "monMonde" sauvegardé.                    │
├─────────────────────────────────────────────────────┤
│  ›  Entrez une commande…                            │
└─────────────────────────────────────────────────────┘
```

Fonctionnalités : historique ↑/↓, défilement auto, 200 lignes max, animations d'ouverture/fermeture.

---

## 🎮 Modes de jeu

| Mode | Description |
|---|---|
| **Créatif** | Blocs infinis, pas de mort, inventaire créatif complet |
| **Survie** | Récolte de ressources, santé, dégâts de chute, craft requis |

Le mode est géré par `GameModeManager` (classe statique). Il revient automatiquement en Créatif au rechargement de scène.

---

## 📁 Structure du projet

```
AstroVoxel/
├── Assets/
│   ├── _Scripts/
│   │   ├── Bootstrap/          — GameBootstrap (point d'entrée)
│   │   ├── Environment/        — AtmosphereRenderer, SpaceSkyboxController, SunOrbit
│   │   ├── Physics/            — GravityAttractor, GravityBody, OzoneLayer
│   │   ├── Player/             — PlayerController, PlayerHealth, Inventaire, Craft, HUD, Console
│   │   ├── Save/               — SaveSystem, WorldSaveData
│   │   ├── Space/              — Planètes infinies, Astéroïdes, Météorites, Seed
│   │   ├── Vehicle/            — SpaceShipController, SpaceShipCamera
│   │   └── VoxelEngine/        — Moteur voxel, génération, biomes, types de blocs
│   ├── _Scenes/
│   │   └── FIRST.unity         — Scène principale (un seul GameObject "Bootstrap")
│   ├── _Shaders/               — Shaders URP personnalisés
│   ├── _Materials/             — Matériaux des blocs
│   ├── _Settings/              — URP Asset, Volume Profile, Network Prefabs
│   ├── LowlyPoly/              — Assets 3D (personnage, vaisseau, etc.)
│   ├── Resources/              — Resources chargées dynamiquement
│   └── textures/               — Atlas de textures des blocs + particules
├── Packages/                   — Dépendances Unity (URP, Input System, Netcode…)
├── ProjectSettings/            — Paramètres Unity
├── AstroVoxel.slnx             — Solution Visual Studio
├── TODO_LIST.md                — Feuille de route du projet
└── ASSET_LIST.md               — Liste des assets Unity Store utilisés
```

---

## 🎨 Assets tiers

Assets depuis l'**Unity Asset Store** :

| Asset | Utilisation |
|---|---|
| [Free Sci-Fi Trooper Man v3](https://assetstore.unity.com/packages/3d/characters/humanoids/sci-fi/free-sci-fi-trooper-man-v3-279548) | Modèle 3D du joueur |
| [Space Shuttle of the Future](https://assetstore.unity.com/packages/3d/vehicles/space/space-shuttle-of-the-future-111392) | Modèle 3D du vaisseau |
| [UFO Battleship](https://assetstore.unity.com/packages/3d/vehicles/space/ufo-battleship-289193) | Ennemis UFO + armes |
| [Space Station Free](https://assetstore.unity.com/packages/3d/vehicles/space/space-station-free-3d-asset-hdrp-urp-built-in-188734) | Station spatiale |
| [Asteroids Low Poly Pack](https://assetstore.unity.com/packages/3d/environments/sci-fi/asteroids-low-poly-pack-142164) | Modèles d'astéroïdes décoratifs |
| [3D Asteroid Pack](https://assetstore.unity.com/packages/3d/environments/sci-fi/3d-asteroid-pack-263841) | Modèles d'astéroïdes supplémentaires |
| [Horror Plush Toys](https://assetstore.unity.com/packages/3d/characters/humanoids/horror-plush-toys-spooky-alien-mascots-255252) | Ennemis aliens |
| [16-Bit Space Adventure Music](https://assetstore.unity.com/packages/audio/music/electronic/16-bit-space-adventure-music-pack-179084) | Musique d'ambiance |

---

## 🗺️ Feuille de route

### Jour 1 ✅
- [x] Planètes 18 faces (cube-sphère octaédrique)
- [x] 30 planètes aléatoires (→ porté à 512 planètes)

### Jour 2 (en cours / à venir)
- [ ] **Joueur — Survie** : Couper du bois → obtenir des bûches
- [ ] **Établi** : Débloque les recettes avancées
- [ ] **Four** : Cuire les ressources (minerais → lingots)

### Fonctionnalités futures envisagées
- [ ] Multijoueur (infrastructure Netcode for GameObjects déjà en place)
- [ ] Ennemis et combats
- [ ] Station spatiale explorable
- [ ] Système de quêtes / objectifs
- [ ] Crafting avancé (four, alchimie)
- [ ] Sons et effets audio complets

---

## 🔧 Lancer le projet

1. **Cloner** le dépôt
2. **Ouvrir** avec Unity (version LTS recommandée avec URP support)
3. **Ouvrir** la scène `Assets/_Scenes/FIRST.unity`
4. **Appuyer sur Play** — la scène se génère entièrement par code

> **Note** : Le seul MonoBehaviour à configurer dans la scène est `GameBootstrap` attaché au GameObject "Bootstrap". Tous les autres systèmes sont instanciés par code depuis `Awake()`.

### Contrôles en jeu

| Action | Touche |
|---|---|
| Déplacement | ZQSD / WASD |
| Regarder | Souris |
| Sauter | Espace |
| Sprint | Shift |
| Accroupi | Ctrl |
| Casser bloc | Clic gauche |
| Poser bloc | Clic droit |
| Hotbar | 1–9 |
| Inventaire | Tab / E |
| Console | T ou / |
| Embarquer vaisseau | F (à proximité) |

---

*AstroVoxel — Un sandbox spatial infini, un bloc à la fois.*
