using UnityEngine;

/// <summary>
/// One-time setup helper that loads ItemData assets from Resources and assigns them
/// to the Inventory. Also creates the arrow projectile prefab at runtime.
/// Attach this to the Player GameObject. It runs once on Start().
/// </summary>
public class ItemSetup : MonoBehaviour
{
    public static ItemSetup Instance;
    public static GameObject MeatTemplate;

    [Header("Drag ItemData assets here to pre-load them into the hotbar")]
    [Tooltip("Items to load into slots 1-9. Empty entries = empty slot.")]
    public ItemData[] StartingItems = new ItemData[5];

void Start()
    {
        Inventory inv = GetComponent<Inventory>();
        if (inv == null)
        {
            Debug.LogError("[ItemSetup] No Inventory found on this GameObject!");
            return;
        }

        // Auto-load all ItemData from Resources/Items folder
        ItemData[] allItems = Resources.LoadAll<ItemData>("Items");
        Debug.Log($"[ItemSetup] Found {allItems.Length} items in Resources/Items");

        // If no items found in Resources, create defaults at runtime
        if (allItems.Length == 0)
        {
            allItems = CreateDefaultItems();
        }

        // Assign prefab references if missing (using name matching)
        foreach (var item in allItems)
        {
            if (item.Prefab == null && !string.IsNullOrEmpty(item.ItemName))
            {
                // Try to find a matching prefab by name
#if UNITY_EDITOR
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Prefabs Weapons/Weapons/{item.ItemName}.prefab");
                if (prefab != null) item.Prefab = prefab;
#endif
            }
        }

        // Also try StartingItems from Inspector (if manually assigned)
        for (int i = 0; i < StartingItems.Length && i < inv.Slots.Length; i++)
        {
            if (StartingItems[i] != null)
            {
                inv.Slots[i] = StartingItems[i];
                inv.SlotCounts[i] = 1;
            }
        }

        // Fill remaining empty slots with auto-loaded items (up to 5 slots to leave room for loot)
        int slot = 0;
        foreach (var item in allItems)
        {
            // Find next empty slot
            while (slot < inv.Slots.Length && inv.Slots[slot] != null) slot++;
            if (slot >= 5) break; // Limit to 5 slots total for starting gear
            
            inv.Slots[slot] = item;
            inv.SlotCounts[slot] = 1;
            slot++;
        }

        // Create arrow projectile if any Ranged item needs one
        foreach (var item in inv.Slots)
        {
            if (item != null && item.Type == ItemType.Ranged && item.ProjectilePrefab == null)
            {
#if UNITY_EDITOR
                GameObject pole = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EmaceArt - raft on the desert/Prefabs/Prop/Pole/EA04_Prop_Pole_01a_PRE.prefab");
                if (pole != null)
                {
                    item.ProjectilePrefab = pole;
                    Debug.Log($"[ItemSetup] Assigned Pole prefab as projectile for {item.ItemName}");
                }
                else
#endif
                {
                    item.ProjectilePrefab = CreateArrowPrefab();
                    Debug.Log($"[ItemSetup] Created default arrow prefab for {item.ItemName}");
                }
            }
        }

        // Create Meat Item and Prefab
        CreateMeatItemAndPrefab(inv);

        // Create Water Bottle Item and Prefab
        CreateWaterBottleItemAndPrefab(inv);

        // Force the inventory to fire its change event for initial equip
        inv.SelectSlot(1);
        inv.SelectSlot(0);

        // Force HotbarController to redraw the new items
        HotbarController ui = FindFirstObjectByType<HotbarController>();
        if (ui != null) ui.RefreshAll();

        // Auto-attach ItemDropper (Q-key drop) if not already present
        if (GetComponent<ItemDropper>() == null)
        {
            gameObject.AddComponent<ItemDropper>();
            Debug.Log("[ItemSetup] Added ItemDropper component (Press Q to drop items)");
        }

        // Auto-attach ItemPlacer (right-click place) if not already present
        if (GetComponent<ItemPlacer>() == null)
        {
            gameObject.AddComponent<ItemPlacer>();
            Debug.Log("[ItemSetup] Added ItemPlacer component (Right-click to place items)");
        }

        Debug.Log($"[ItemSetup] Item system initialized with {inv.Slots.Length} slots!");
    }

    private void CreateWaterBottleItemAndPrefab(Inventory inv)
    {
        // 1. Create the Water Bottle ItemData
        ItemData water = ScriptableObject.CreateInstance<ItemData>();
        water.ItemName = "Water Bottle";
        water.Type = ItemType.Consumable;
        water.Damage = 0;
        water.MaxStack = 10;
        water.ThirstRestore = 30f;
        water.HealthRestore = 0f;
        
        // 2. Create the Water Bottle Prefab (Cylinder)
        GameObject bottle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bottle.name = "WaterBottleDrop";
        bottle.transform.localScale = new Vector3(0.15f, 0.25f, 0.15f);

        // Add LootItem component
        LootItem loot = bottle.AddComponent<LootItem>();
        loot.Data = water;
        loot.PickupRadius = 4.0f;

        // Add Outline
        bottle.AddComponent<OutlineController>();

        // Give it a blue color
        Renderer rend = bottle.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            
            Material waterMat = new Material(litShader != null ? litShader : rend.sharedMaterial.shader);
            Color blueWater = new Color(0.1f, 0.4f, 0.8f, 0.8f);
            if (waterMat.HasProperty("_BaseColor"))
                waterMat.SetColor("_BaseColor", blueWater);
            else if (waterMat.HasProperty("_Color"))
                waterMat.SetColor("_Color", blueWater);
                
            rend.material = waterMat;
        }

        // Add to inventory if there's space (slot 3 or 4)
        for (int i = 0; i < inv.Slots.Length; i++)
        {
            if (inv.Slots[i] == null)
            {
                inv.Slots[i] = water;
                inv.SlotCounts[i] = 1;
                break;
            }
        }

        // Assign prefab to data
        water.Prefab = bottle;
        
        // Keep it alive as template
        DontDestroyOnLoad(bottle);
        bottle.SetActive(false);
    }

    private void CreateMeatItemAndPrefab(Inventory inv)
    {
        // 1. Create the Meat ItemData
        ItemData meat = ScriptableObject.CreateInstance<ItemData>();
        meat.ItemName = "Meat";
        meat.Type = ItemType.Consumable;
        meat.Damage = 0;
        meat.MaxStack = 10;
        meat.HungerRestore = 10f;
        meat.HealthRestore = 2f;
        
        // 2. Load the Meat Prefab from Assets
        GameObject meatPrefab = null;
#if UNITY_EDITOR
        meatPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Meat/Meat.prefab");
#endif

        // Fallback to sphere if prefab not found
        if (meatPrefab == null)
        {
            meatPrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            meatPrefab.name = "MeatDrop_Fallback";
        }
        else
        {
            // Instantiate a copy so we can modify it without changing the asset permanently in every session if desired, 
            // but usually we want to keep it as a template.
            GameObject template = Instantiate(meatPrefab);
            template.name = "MeatDrop";
            meatPrefab = template;
        }

        meatPrefab.transform.localScale = new Vector3(0.5f, 0.3f, 0.5f);

        // Add LootItem component
        LootItem loot = meatPrefab.AddComponent<LootItem>();
        loot.Data = meat;
        loot.PickupRadius = 4.0f;

        // Add Outline
        meatPrefab.AddComponent<OutlineController>();

        // Ensure Rigidbody is preserved for physics (toss and gravity)
        Rigidbody rb = meatPrefab.GetComponent<Rigidbody>();
        // if (rb != null) DestroyImmediate(rb);

        // Ensure it has a collider for surface detection if needed, 
        // but LootItem uses distance anyway. Prefab already has CapsuleCollider.

        // Give it a more vibrant red color
        Renderer rend = meatPrefab.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            if (litShader == null && rend.sharedMaterial != null) litShader = rend.sharedMaterial.shader;

            if (litShader != null)
            {
                Material meatMat = new Material(litShader);
                Color vibrantRed = new Color(1.0f, 0.05f, 0.05f); // Vibrant Red
                if (meatMat.HasProperty("_BaseColor"))
                    meatMat.SetColor("_BaseColor", vibrantRed);
                else if (meatMat.HasProperty("_Color"))
                    meatMat.SetColor("_Color", vibrantRed);
                
                // Make it look a bit more "meaty" / juicy
                if (meatMat.HasProperty("_Smoothness"))
                    meatMat.SetFloat("_Smoothness", 0.6f);
                if (meatMat.HasProperty("_Metallic"))
                    meatMat.SetFloat("_Metallic", 0.1f);
                    
                rend.material = meatMat;
            }
        }

        // Specifically remove any legacy scripts that might conflict
        MonoBehaviour[] scripts = meatPrefab.GetComponentsInChildren<MonoBehaviour>();
        foreach (var script in scripts)
        {
            if (script != null && 
                script != loot && 
                !(script is OutlineController))
            {
                DestroyImmediate(script);
            }
        }

        // Assign prefab to data
        meat.Prefab = meatPrefab;
        
        // Fix NaN error: Explicitly initialize Hold properties for runtime-created ItemData
        meat.HoldPosition = new Vector3(0.4f, -0.4f, 0.7f);
        meat.HoldRotation = new Vector3(-20, 0, 0);
        meat.HoldScale = 1.0f;
        
        MeatTemplate = meatPrefab;

        // Keep it alive
        DontDestroyOnLoad(meatPrefab);
        meatPrefab.SetActive(false); // Template

        Debug.Log("[ItemSetup] Meat system initialized and template created.");
    }

/// <summary>
    /// Creates default items at runtime when no ItemData assets exist in Resources.
    /// This ensures the system works out-of-the-box without manual asset setup.
    /// </summary>
    private ItemData[] CreateDefaultItems()
    {
        // Dagger
        ItemData dagger = ScriptableObject.CreateInstance<ItemData>();
        dagger.ItemName = "Dagger";
        dagger.Type = ItemType.Melee;
        dagger.Damage = 15f;
        dagger.AttackSpeed = 2.5f;
        dagger.Range = 1.5f;
        dagger.HoldPosition = new Vector3(0, 0, 0.1f);
        dagger.HoldRotation = new Vector3(-90, 180, 0);
        dagger.HoldScale = 1.2f;

        // Bow
        ItemData bow = ScriptableObject.CreateInstance<ItemData>();
        bow.ItemName = "Bow";
        bow.Type = ItemType.Ranged;
        bow.Damage = 25f;
        bow.AttackSpeed = 0.8f;
        bow.Range = 80f;
        bow.ProjectileSpeed = 40f;
        bow.HoldPosition = new Vector3(-0.7f, -0.1f, 0.3f);
        bow.HoldRotation = new Vector3(0, 90, 0);
        bow.HoldScale = 1.5f;
#if UNITY_EDITOR
        bow.ProjectilePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EmaceArt - raft on the desert/Prefabs/Prop/Pole/EA04_Prop_Pole_01a_PRE.prefab");
#endif

        // Katana
        ItemData katana = ScriptableObject.CreateInstance<ItemData>();
        katana.ItemName = "Katana";
        katana.Type = ItemType.Melee;
        katana.Damage = 30f;
        katana.AttackSpeed = 1.2f;
        katana.Range = 2.5f;
        katana.HoldPosition = new Vector3(0, 0, 0.15f);
        katana.HoldRotation = new Vector3(-90, 180, 0);
        katana.HoldScale = 1.5f;

        // Try to load weapon prefabs from the Prefabs folder
#if UNITY_EDITOR
        dagger.Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Prefabs Weapons/Weapons/Dagger.prefab");
        bow.Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Prefabs Weapons/Weapons/Bow.prefab");
        katana.Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Prefabs Weapons/Weapons/Katana.prefab");
#endif

        return new ItemData[] { dagger, bow, katana };
    }


    /// <summary>
    /// Creates a simple arrow prefab at runtime for ranged weapons.
    /// </summary>
    private GameObject CreateArrowPrefab()
    {
        // Create arrow base
        GameObject arrow = new GameObject("ArrowProjectile");
        arrow.SetActive(false); // Template — will be instantiated

        // Shaft (cylinder)
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.transform.SetParent(arrow.transform);
        shaft.transform.localPosition = Vector3.zero;
        shaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
        shaft.transform.localScale = new Vector3(0.03f, 0.25f, 0.03f);

        // Tip (cube scaled to point)
        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tip.transform.SetParent(arrow.transform);
        tip.transform.localPosition = new Vector3(0, 0, 0.3f);
        tip.transform.localRotation = Quaternion.Euler(0, 0, 45);
        tip.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);

        // Remove default colliders from visual parts (Projectile.cs adds its own)
        foreach (var col in arrow.GetComponentsInChildren<Collider>())
        {
            Destroy(col);
        }

        // Find an appropriate shader safely
        Shader arrowShader = Shader.Find("Universal Render Pipeline/Lit");
        if (arrowShader == null)
        {
            arrowShader = Shader.Find("Standard");
        }

        // Arrow material
        Renderer[] renderers = arrow.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (arrowShader != null)
            {
                Material mat = new Material(arrowShader);
                mat.color = new Color(0.4f, 0.25f, 0.1f); // Wood brown
                r.material = mat;
            }
        }

        // Add projectile component
        Projectile proj = arrow.AddComponent<Projectile>();
        proj.Lifetime = 10f;
        proj.StickOnHit = true;
        proj.GravityScale = 0.3f;

        // Add collider for the projectile
        CapsuleCollider cap = arrow.AddComponent<CapsuleCollider>();
        cap.radius = 0.05f;
        cap.height = 0.5f;
        cap.direction = 2; // Z axis

        // Get rigidbody (automatically added by Projectile requirement)
        Rigidbody rb = arrow.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Don't destroy — we'll use this as a template
        DontDestroyOnLoad(arrow);

        return arrow;
    }
}
