using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class TreeZoneTreeSpawnEntry
{
    public GameObject Prefab;
    [Range(0f, 1f)]
    public float SpawnChance = 1.0f;
    public Vector2Int CountRange = new Vector2Int(1, 1);
    public float ScaleMultiplier = 5.0f;
    public float GroundingOffset = 0.1f;
    public float MinSpacing = 10.0f;

    public void RepairUnsetValues(float fallbackScaleMultiplier, float fallbackGroundingOffset, float fallbackMinSpacing)
    {
        if (SpawnChance <= 0f && Prefab == null)
            SpawnChance = 1.0f;

        if (CountRange.x <= 0 && CountRange.y <= 0)
            CountRange = new Vector2Int(1, 1);

        if (ScaleMultiplier <= 0f)
            ScaleMultiplier = Mathf.Max(0.01f, fallbackScaleMultiplier);

        if (Mathf.Approximately(GroundingOffset, 0f) && fallbackGroundingOffset > 0f)
            GroundingOffset = fallbackGroundingOffset;

        if (MinSpacing <= 0f)
            MinSpacing = Mathf.Max(0.01f, fallbackMinSpacing);
    }
}

[CreateAssetMenu(fileName = "TerrainConfig", menuName = "Sand/Terrain Config")]
public class TerrainConfig : ScriptableObject
{
    [Header("Generation")]
    [Tooltip("If true, a random seed will be generated every time you hit Play.")]
    public bool RandomizeSeedOnPlay = true;
    public int Seed = 12345;
    public float NoiseScale = 0.005f;
    public float HeightMultiplier = 35.0f;
    public float BaseHeight = 5.0f;

    [Header("Simulation")]
    [Tooltip("Size of one chunk in grid cells (excluding overlap).")]
    public int ChunkSize = 64; 
    [Tooltip("World space size of one grid cell.")]
    public float CellSize = 1.0f;
    [Tooltip("How steep the sand can pile up.")]
    public float FlowThreshold = 1.0f;
    [Tooltip("Speed of sand flow.")]
    public float FlowSpeed = 0.1f;
    [Tooltip("How long sand keeps flowing after digging or placing sand.")]
    public float FlowDurationAfterEdit = 2.5f;
    [Tooltip("Depth of the volume skirt (visual only).")]
    public float BottomDepth = 10.0f;
    [Header("Roof Sand Stabilization")]
    [Tooltip("If true, placed Roof objects stop sand from flowing where they touch or support it.")]
    public bool RootSandStabilizationEnabled = true;
    [Tooltip("Distance from the Roof collider that counts as direct contact with sand.")]
    public float RootSandContactDistance = 0.35f;
    [Tooltip("Extra vertical space above a Roof collider footprint where sand is treated as supported.")]
    public float RootSandAbovePadding = 0.5f;
    [Tooltip("Extra radius around Roof contact where sand flow is slowed instead of fully stopped.")]
    public float RootSandEdgeSlowRadius = 1.0f;
    [Tooltip("Flow multiplier for sand near, but not directly touching, a Roof.")]
    [Range(0f, 1f)] public float RootSandEdgeFlowMultiplier = 0.15f;

    [Header("Streaming")]
    [Tooltip("Radius of active simulation chunks around player.")]
    public int LoadingRadius = 3;
    [Tooltip("Distance at which the mesh collider is enabled.")]
    public float ColliderLODDistance = 120.0f;
    [Tooltip("Distance at which sand simulation occurs.")]
    public float SimulationLODDistance = 200.0f;
    [Tooltip("Radius around the player to load vegetation (Joshua Trees, bushes, grass). Should be smaller than MountainLoadingRadius for performance.")]
    public int VegetationLoadingRadius = 4;
    [Tooltip("World-space radius around the player where pickup real grass is loaded. Other vegetation still uses VegetationLoadingRadius.")]
    public float RealGrassLoadRadiusMeters = 150f;
    
    [Header("Rendering")]
    public Material SandMaterial;
    public Color SandColor = new Color(0.82f, 0.55f, 0.25f); // Golden Sand

    [Header("Apron Sand (Mountain Base)")]
    [Tooltip("Material using the Custom/SandBlend shader. If set, overrides SandMaterial for chunks.")]
    public Material ApronSandMaterial;
    [Tooltip("Texture for the hardened sand near mountain bases.")]
    public Texture2D ApronSandTexture;
    [Tooltip("Tint color for the apron sand texture.")]
    public Color ApronSandColor = new Color(0.6f, 0.45f, 0.3f, 1f);

    [Header("Mountains & Stones")]
    [Tooltip("IF TRUE, mountain spawn chance becomes high (0.1) and player spawns near first mountain.")]
    public bool MountainTestMode = false;
    [Tooltip("If null, a procedural mountain mesh will be generated.")]
    public GameObject MountainPrefab; 
    public Material MountainMaterial;
    
    [Tooltip("Paths to rock prefabs to randomly scatter as stones.")]
    public List<string> StonePrefabPaths = new List<string> {
        "Assets/Prefabs/rock/Rock_01.prefab",
        "Assets/Prefabs/rock/Rock_02.prefab",
        "Assets/Prefabs/rock/Rock_03.prefab",
        "Assets/Prefabs/rock/Rock_04.prefab",
        "Assets/Prefabs/rock/Rock_05.prefab",
        "Assets/Prefabs/rock/rock07_m.prefab"
    };

    [System.NonSerialized]
    public List<GameObject> LoadedStonePrefabs = new List<GameObject>();
    
    [Tooltip("Probability of a massive mountain spawning per chunk (e.g. 0.005 for 1 in 200 chunks)")]
    public float MountainSpawnChance = 0.005f; // 1 in 200
    [Tooltip("Probability of a smaller stone spawning per chunk (e.g. 0.02 for 1 in 50 chunks)")]
    public float StoneSpawnChance = 0.02f;   // 1 in 50
    
    [Tooltip("Radius around the player to load mountains and stones. Should be larger than LoadingRadius.")]
    public int MountainLoadingRadius = 10;
    
    public float MountainMinScale = 800f; // Massive
    public float MountainMaxScale = 1500f; // Bigger
    
    [Tooltip("Fraction of mountain height to sink into the ground. 0.8 = 80% buried, only 20% head visible. Now multiplied by scale.")]
    public float MountainGroundingOffset = 3.0f;
    
    [Tooltip("The radius of the mountain mesh at scale 1.0 (half of the largest horizontal bounds extent). Used to calculate where the flat terrain zone begins. Measure from your prefab's renderer bounds.")]
    public float MountainMeshRadiusMultiplier = 19.0f;
    
    public float StoneMinScale = 30f;
    public float StoneMaxScale = 80f;

    [Tooltip("Stones will only spawn if surface height is within this range.")]
    public float StoneMinSpawnHeight = -100f;
    public float StoneMaxSpawnHeight = 1000f;

    [Tooltip("Independent vertical scale randomization for stones.")]
    public float StoneMinHeightScale = 0.5f;
    public float StoneMaxHeightScale = 1.5f;

    [Tooltip("Amount to sink stones into the ground. Increase if stones are floating.")]
    public float StoneGroundingOffset = 0.1f;
    [Tooltip("Manual list of stone prefabs for direct assignment.")]
    public List<GameObject> StonePrefabs = new List<GameObject>();

    [Header("Mini Stones")]
    [Tooltip("List of smaller stones meant to scatter like plants.")]
    public List<GameObject> MiniStonePrefabs = new List<GameObject>();
    [Tooltip("Probability of mini stones spawning per chunk.")]
    public float MiniStoneSpawnChance = 0.5f;
    [Tooltip("Number of mini stones to spawn when successful.")]
    public int MiniStonesPerChunk = 2;
    public float MiniStoneMinScale = 0.5f;
    public float MiniStoneMaxScale = 1.5f;
    public float MiniStoneGroundingOffset = 0.05f;

    [Header("Small Branches")]
    [Tooltip("List of small branch prefabs meant to scatter like mini stones.")]
    public List<GameObject> SmallBranchPrefabs = new List<GameObject>();
    [Tooltip("Probability of small branches spawning per chunk.")]
    public float SmallBranchSpawnChance = 0.5f;
    [Tooltip("Number of small branches to spawn when successful.")]
    public int SmallBranchesPerChunk = 2;
    public float SmallBranchMinScale = 0.5f;
    public float SmallBranchMaxScale = 1.5f;
    public float SmallBranchGroundingOffset = 0.05f;

    [Header("Mountain Vegetation (Apron)")]
    public List<GameObject> JoshuaTreePrefabs = new List<GameObject>();
    public List<GameObject> MountainBushPrefabs = new List<GameObject>();
    public List<GameObject> MountainGrassPrefabs = new List<GameObject>();
    
    public Vector2Int MountainBushCountPerChunk = new Vector2Int(5, 12);
    public Vector2Int MountainGrassCountPerChunk = new Vector2Int(15, 30);
    
    public float MountainBushScale = 1.0f;
    public float MountainGrassScale = 0.8f;

    [Tooltip("Noise scale for the clumping effect of apron vegetation.")]
    public float MountainVegClumpScale = 0.05f;
    [Tooltip("Threshold for vegetation clumps (0-1). Lower = more vegetation.")]
    public float MountainVegClumpThreshold = 0.45f;

    [Header("Terrain Grass")]
    [Tooltip("Grass prefabs to scatter across all regular terrain chunks.")]
    public List<GameObject> TerrainGrassPrefabs = new List<GameObject>();
    [Tooltip("Number of grass formation spawn points to try per terrain chunk.")]
    public Vector2Int TerrainGrassCountPerChunk = new Vector2Int(20, 35);
    public float TerrainGrassScale = 1.0f;
    public float TerrainGrassGroundingOffset = -0.25f;
    [Range(0f, 1f)]
    [Tooltip("Chance that each successful grass spawn point creates one blade instead of a group.")]
    public float TerrainGrassSoloFormationChance = 0.8f;
    [Tooltip("Smallest scatter radius used when a grass group forms.")]
    public float TerrainGrassGroupRadiusMin = 0.3f;
    [Tooltip("Largest scatter radius used when a grass group forms.")]
    public float TerrainGrassGroupRadiusMax = 0.8f;

    [Header("Tree Forming Zones")]
    [Range(0f, 1f)]
    [Tooltip("Chance that any vegetation chunk becomes a flat tree-forming zone. 0.05 is 1 in 20 chunks.")]
    public float TreeZoneSpawnChance = 0.05f;
    [Tooltip("If true, tree-forming zones use deterministic 1000m spacing measured from the highway flat corridor.")]
    public bool TreeZoneUseHighwayFlatSpacing = true;
    [Tooltip("World-space interval between tree-forming zones, measured from the outer edge of the highway flat corridor.")]
    public float TreeZoneSpacingFromFlatMeters = 1000.0f;
    [Tooltip("Minimum base Y level for tree-forming zones. Set both min and max to 8 for a fixed Y 8 zone.")]
    public float TreeZoneMinBaseHeight = 8.0f;
    [Tooltip("Maximum base Y level for tree-forming zones. Different min/max values make each zone pick a seeded height in this range.")]
    public float TreeZoneMaxBaseHeight = 8.0f;
    [Tooltip("Per-prefab tree-zone spawn settings. Each entry rolls independently in a tree-forming chunk.")]
    public List<TreeZoneTreeSpawnEntry> TreeZoneTreeSpawnEntries = new List<TreeZoneTreeSpawnEntry>();
    [Tooltip("Tree prefabs to scatter inside tree-forming zones. Drag & Drop here.")]
    public List<GameObject> TreeZoneTreePrefabs = new List<GameObject>();
    [Tooltip("Fallback count used only by TreeZoneTreePrefabs when no per-prefab entries are assigned.")]
    public Vector2Int TreeZoneTreeCountPerChunk = new Vector2Int(1, 1);
    [Tooltip("Fallback prefab scale multiplier used only by TreeZoneTreePrefabs when no per-prefab entries are assigned.")]
    public float TreeZoneTreeScaleMultiplier = 5.0f;
    public float TreeZoneTreeGroundingOffset = 0.1f;
    [Tooltip("Minimum spacing between trees inside a tree-forming chunk.")]
    public float TreeZoneTreeMinSpacing = 10.0f;
    [Tooltip("Number of mini stones to spawn inside each tree-forming zone.")]
    public Vector2Int TreeZoneMiniStoneCountPerZone = new Vector2Int(10, 20);
    [Tooltip("Minimum full width, in meters, for each square tree-forming zone. Can be larger than one terrain chunk.")]
    public float TreeZoneMinAreaMeters = 63.0f;
    [Tooltip("Maximum full width, in meters, for each square tree-forming zone. Each zone picks a seeded width between min and max.")]
    public float TreeZoneMaxAreaMeters = 63.0f;
    [Tooltip("World-space distance used to fade the edge of the tree-forming zone into normal dunes.")]
    public float TreeZoneEdgeBlendMeters = 8.0f;
    [Tooltip("World-space distance used only for fading tree-zone ground textures. Higher values create a longer, softer texture gradient.")]
    public float TreeZoneTextureEdgeBlendMeters = 35.0f;
    [HideInInspector]
    [Tooltip("Extra world-space padding from the soft edge of each tree-forming chunk. Lower values allow trees closer to the edge; higher values shrink the spawn area.")]
    public float TreeZoneTreeSpawnEdgePadding = 2.0f;
    [HideInInspector]
    [Tooltip("Minimum tree-zone influence required before trees can spawn. Lower values make the usable tree-forming area larger.")]
    public float TreeZoneTreeSpawnInfluenceThreshold = 0.75f;
    [Range(0f, 1f)]
    [Tooltip("How strongly selected tree-forming chunks flatten toward their calm center height.")]
    public float TreeZoneFlatStrength = 1.0f;
    [HideInInspector]
    [Tooltip("Fraction of the chunk width used to blend flat tree-zone ground into normal dunes at the edge.")]
    public float TreeZoneEdgeBlend = 0.18f;
    [Tooltip("How far outside a tree-forming chunk the surrounding sand becomes low-wave and almost flat.")]
    public float TreeZoneLowWaveRadius = 150.0f;
    [Range(0f, 1f)]
    [Tooltip("How strongly the surrounding tree-zone sand suppresses normal dunes into low waves.")]
    public float TreeZoneLowWaveStrength = 0.9f;
    [Tooltip("World-space distance used to fade the 150m low-wave area back into normal dunes.")]
    public float TreeZoneLowWaveEdgeBlend = 45.0f;
    [Tooltip("If true, realgrass entries in TreeZoneTreeSpawnEntries use dense gradient spawning instead of tree spacing.")]
    public bool TreeZoneGrassGradientEnabled = true;
    [Tooltip("World-space distance outside a tree-forming chunk where realgrass can fade across the low-wave flat sand.")]
    public float TreeZoneGrassOuterRadius = 150.0f;
    [Tooltip("How strongly tree-zone grass placement is pulled toward the center. Lower values spread the same count outward.")]
    public float TreeZoneGrassCenterBiasPower = 1.85f;
    [Range(0f, 1f)]
    [Tooltip("Spawn acceptance density near the tree-forming chunk center.")]
    public float TreeZoneGrassCenterDensity = 1.0f;
    [Range(0f, 1f)]
    [Tooltip("Spawn acceptance density near the outer edge of the tree-zone grass radius.")]
    public float TreeZoneGrassOuterDensity = 0.15f;
    [Tooltip("How many placement attempts to make per desired tree-zone grass blade.")]
    public int TreeZoneGrassSpawnAttemptsMultiplier = 3;
    [Tooltip("Smallest scatter offset used to soften dense tree-zone grass placement.")]
    public float TreeZoneGrassGroupRadiusMin = 0.3f;
    [Tooltip("Largest scatter offset used to soften dense tree-zone grass placement.")]
    public float TreeZoneGrassGroupRadiusMax = 0.8f;
    public Texture2D TreeZoneGroundTextureA;
    public Texture2D TreeZoneGroundTextureB;
    public Texture2D TreeZoneGroundTextureC;
    public Color TreeZoneGroundColor = Color.white;
    public float TreeZoneTextureTiling = 0.35f;

    private void OnValidate()
    {
        NormalizeTreeZoneAreaMeters();
        RepairTreeZoneEntries();
    }

    private void NormalizeTreeZoneAreaMeters()
    {
        RealGrassLoadRadiusMeters = Mathf.Max(0f, RealGrassLoadRadiusMeters);
        TreeZoneMinAreaMeters = Mathf.Max(1.0f, TreeZoneMinAreaMeters);
        TreeZoneMaxAreaMeters = Mathf.Max(1.0f, TreeZoneMaxAreaMeters);

        if (TreeZoneMinAreaMeters > TreeZoneMaxAreaMeters)
        {
            float oldMin = TreeZoneMinAreaMeters;
            TreeZoneMinAreaMeters = TreeZoneMaxAreaMeters;
            TreeZoneMaxAreaMeters = oldMin;
        }

        TreeZoneEdgeBlendMeters = Mathf.Clamp(TreeZoneEdgeBlendMeters, 0.01f, TreeZoneMaxAreaMeters * 0.5f);
        TreeZoneTextureEdgeBlendMeters = Mathf.Max(0.01f, TreeZoneTextureEdgeBlendMeters);
        TreeZoneSpacingFromFlatMeters = Mathf.Max(1.0f, TreeZoneSpacingFromFlatMeters);
        TreeZoneGrassCenterBiasPower = Mathf.Max(0.01f, TreeZoneGrassCenterBiasPower);
    }

    private void RepairTreeZoneEntries()
    {
        if (TreeZoneTreeSpawnEntries == null)
            return;

        float fallbackScale = TreeZoneTreeScaleMultiplier > 0f ? TreeZoneTreeScaleMultiplier : 5.0f;
        float fallbackGrounding = TreeZoneTreeGroundingOffset > 0f ? TreeZoneTreeGroundingOffset : 0.1f;
        float fallbackSpacing = TreeZoneTreeMinSpacing > 0f ? TreeZoneTreeMinSpacing : 10.0f;

        for (int i = 0; i < TreeZoneTreeSpawnEntries.Count; i++)
        {
            TreeZoneTreeSpawnEntry entry = TreeZoneTreeSpawnEntries[i];
            if (entry == null)
            {
                entry = new TreeZoneTreeSpawnEntry();
                TreeZoneTreeSpawnEntries[i] = entry;
            }

            entry.RepairUnsetValues(fallbackScale, fallbackGrounding, fallbackSpacing);
        }
    }

    [Header("Plants")]
    [Tooltip("Plant (tree/cactus) prefabs to randomly scatter. Drag & Drop here.")]
    public List<GameObject> LoadedPlantPrefabs = new List<GameObject>();

    [Tooltip("Probability of a plant spawning per chunk.")]
    public float PlantSpawnChance = 0.05f;

    public float PlantMinScale = 1.0f;
    public float PlantMaxScale = 2.5f;

    [Tooltip("Amount to sink plants into the ground.")]
    public float PlantGroundingOffset = 0.05f;

    [Header("Joshua Trees")]
    [Tooltip("Joshua tree prefabs to scatter on mountain aprons. Drag & Drop here.")]
    public List<GameObject> LoadedJoshuaTreePrefabs = new List<GameObject>();

    [Tooltip("Probability of trying to spawn a Joshua Tree per chunk. Set high since it will only spawn if it hits a flat apron.")]
    public float JoshuaTreeSpawnChance = 1.0f;

    [Tooltip("Number of coordinate attempts per chunk to find a place for a Joshua Tree.")]
    public int JoshuaTreeSpawnAttempts = 20;

    public float JoshuaTreeMinScale = 1.5f;
    public float JoshuaTreeMaxScale = 3.5f;
    public float JoshuaTreeGroundingOffset = 0.1f;

    [Header("Safe Zones")]
    [Tooltip("Radius around the spawn point where stones and plants will NOT spawn.")]
    public float SpawnSafeRadius = 0.0f;
    [Tooltip("Radius around the spawn point where mountains will NOT spawn. Should be larger than SpawnSafeRadius to account for mountain scale.")]
    public float MountainSafeRadius = 2000.0f;
    [Tooltip("The initial spawn point of the player.")]
    public Vector3 PlayerSpawnPoint = new Vector3(50, 40, 50);

    [Header("Mountain Influence")]
    [Tooltip("The width of the perfectly flat ring around a mountain base.")]
    public float MountainFlatRadius = 32.0f;
    [Tooltip("The extra width of the 'low wave/hard sand' ring extending beyond the flat ring (in world units). 64 is approx 1 chunk.")]
    public float MountainInfluenceExtraRadius = 64.0f;
    [Tooltip("How much noise is left near a mountain (0.15 = 15% wave height).")]
    public float MountainFlatteningFactor = 0.15f;
    [Tooltip("How much noise frequency is changed near a mountain (e.g. 0.5 = half frequency).")]
    public float MountainNoiseScaleMultiplier = 0.5f;
    [Tooltip("The noise scale for the 'low wave' ripple pattern near mountains.")]
    public float SecondaryNoiseScale = 0.02f;

    [Header("Mountain Edge Details")]
    [Tooltip("Height multiplier of the low-wave micro noise (ripples).")]
    public float MicroWaveHeight = 0.6f;
    [Tooltip("Maximum height of localized sand pileups strictly against mountain boundaries.")]
    public float MountainPileupMaxHeight = 14.0f;
    [Tooltip("Size/frequency of the directional noise patches for the pileup effect.")]
    public float MountainPileupNoiseScale = 0.015f;

    [Tooltip("How much digging power is left near a mountain (0.2 = -80% speed).")]
    public float MountainHardnessFactor = 0.2f;
    [Tooltip("Player movement speed multiplier when walking on mountain-influenced ground.")]
    public float MountainSpeedMultiplier = 0.7f;

    [Header("Oasis")]
    [Tooltip("IF TRUE, oasis spawn chance becomes 0.1 (10%) for testing. Set FALSE for production.")]
    public bool OasisTestMode = false;
    [Tooltip("Probability of an oasis spawning per chunk (used when OasisTestMode is false).")]
    public float OasisSpawnChance = 0.0001f;
    [Tooltip("Total radius of the oasis basin (outer edge where sand starts dipping).")]
    public float OasisBasinRadius = 100.0f;
    [Tooltip("Radius of the water surface plane (must be < OasisBasinRadius).")]
    public float OasisWaterRadius = 50.0f;
    [Tooltip("Maximum depth of the basin dip below normal terrain height.")]
    public float OasisBasinDepth = 5.0f;
    [Tooltip("Height of the water level relative to the bottom of the basin (center point).")]
    public float OasisWaterHeightOffset = 3.0f;
    [Tooltip("Radius around PlayerSpawnPoint where oases will NOT spawn.")]
    public float OasisSafeRadius = 200.0f;
    [Tooltip("Minimum distance between an oasis center and any mountain center.")]
    public float OasisMountainMinDistance = 500.0f;
    [Tooltip("Number of palm trees to spawn around the oasis water.")]
    public int OasisPalmCount = 30;
    [Tooltip("Number of bushes to spawn clustered tightly around the palm trees.")]
    public int OasisBushCount = 100;
    
    [Tooltip("Manual Y-axis offset to lift bushes out of the sand.")]
    public float OasisBushHeightOffset = 0.2f;

    [Tooltip("Distance from player to a palm tree at which bushes start forming from the sand.")]
    public float BushSpawnTriggerDistance = 30.0f;
    [Tooltip("How long (seconds) it takes for a bush to fully grow from the sand.")]
    public float BushGrowDuration = 1.2f;
    [Tooltip("Number of bushes to spawn around each palm tree when player approaches.")]
    public int BushesPerPalm = 4;

    [Header("Oasis Rim (Elevated Shore)")]
    [Tooltip("Maximum height of the circular ridge around the oasis.")]
    public float OasisRimHeight = 6.0f;
    [Tooltip("How far the ridge extends into the desert.")]
    public float OasisRimWidth = 40.0f;

    [Header("Oasis Assets")]
    public GameObject OasisWaterPrefab;
    public List<GameObject> OasisPalmPrefabs = new List<GameObject>();
    public List<GameObject> OasisBushPrefabs = new List<GameObject>();

    [Header("Oasis Fish")]
    [Tooltip("Number of fish to spawn in the oasis water.")]
    public int OasisFishCountPerOasis = 12;
    [Tooltip("Maximum depth from the water surface where fish will swim.")]
    public float OasisFishMaxSwimDepth = 4.0f;
    
    public List<GameObject> OasisFishPrefabs = new List<GameObject>();
}
