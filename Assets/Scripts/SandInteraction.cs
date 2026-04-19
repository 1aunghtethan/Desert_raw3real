using UnityEngine;

public class SandInteraction : MonoBehaviour
{
    public float BrushStrength = 20.0f;
    public float BrushRadius = 1.0f;
    
    private PlayerController _player;

    void Start()
    {
        _player = GetComponentInParent<PlayerController>();
        if (_player == null) _player = FindFirstObjectByType<PlayerController>();
    }

    void Update()
    {
        // Check if a weapon is equipped — if so, EquipmentHolder handles the click
        EquipmentHolder equipment = EquipmentHolder.Instance;
        bool hasWeaponEquipped = equipment != null 
            && equipment.CurrentItem != null 
            && (equipment.CurrentItem.Type == ItemType.Melee || equipment.CurrentItem.Type == ItemType.Ranged);

        // If a weapon is equipped, don't dig — the weapon handles its own behavior
        if (hasWeaponEquipped) return;

        // If explicitly in placement mode, do not process sand interactions
        ItemPlacer placer = _player != null ? _player.GetComponent<ItemPlacer>() : null;
        if (placer != null && placer.IsPlacementModeActive) return;

        // If a consumable/tool is equipped and right-click is pressed, let ItemPlacer handle it
        bool hasNonDigItem = equipment != null 
            && equipment.CurrentItem != null 
            && equipment.CurrentItem.Type == ItemType.Consumable;
        if (hasNonDigItem && Input.GetMouseButtonDown(1)) return;

        bool bDig = Input.GetMouseButton(0) && !Input.GetKey(KeyCode.LeftShift);
        bool bPlace = (Input.GetMouseButton(0) && Input.GetKey(KeyCode.LeftShift)) || Input.GetMouseButton(1);
        
        if (bDig || bPlace)
        {
            // Use the active camera from PlayerController, fallback to Camera.main
            Camera cam = (_player != null) ? _player.GetActiveCamera() : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            
            // Use RaycastAll to pass through the player's collider and hit terrain
            RaycastHit[] hits = Physics.RaycastAll(ray, 200.0f);
            
            // Find the closest SandChunk hit
            float closestDist = float.MaxValue;
            RaycastHit? bestHit = null;
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponent<SandChunk>() != null && hit.distance < closestDist)
                {
                    closestDist = hit.distance;
                    bestHit = hit;
                }
            }

            if (bestHit.HasValue)
            {
                // Tool items multiply dig power
                float strengthMult = 1f;
                float radiusMult = 1f;
                if (equipment != null && equipment.CurrentItem != null && equipment.CurrentItem.Type == ItemType.Tool)
                {
                    strengthMult = equipment.CurrentItem.DigMultiplier;
                    radiusMult = equipment.CurrentItem.RadiusMultiplier;
                }

                float amount = BrushStrength * strengthMult * Time.deltaTime;
                if (bDig) amount = -amount;
                
                if (TerrainManager.Instance != null)
                {
                    // PLANT PROTECTION: Can't dig near any plant/tree/bush
                    // Using a 2.5m safety radius to ensure you can't dig right beside the roots
                    // which would cause the sand to simulate and slide out from beneath the tree!
                    bool nearPlant = false;
                    Collider[] nearbyCols = Physics.OverlapSphere(bestHit.Value.point, 2.5f);
                    foreach (var col in nearbyCols)
                    {
                        if (col.GetComponent<SandChunk>() != null) continue; // Ignore sand
                        if (col.CompareTag("Player") || col.GetComponentInParent<PlayerController>()) continue; // Ignore player

                        // We check the root name because colliders are often deeply nested inside the plant prefab
                        string n = col.transform.root.name.ToLower();
                        if (col.GetComponentInParent<PlantPhysics>() != null || 
                            n.Contains("bush") || n.Contains("palm") || 
                            n.Contains("tree") || n.Contains("plant") || n.Contains("cactus"))
                        {
                            nearPlant = true;
                            break;
                        }
                    }

                    if (nearPlant) return; // Cancel dig if near a plant

                    // Apply mountain hardness resistance (making sand up to 80% harder to dig)
                    float influence = 0f;
                    if (MountainSpawner.Instance != null)
                    {
                        influence = MountainSpawner.Instance.GetMountainInfluenceAtPoint(bestHit.Value.point);
                    }
                    float hardMult = Mathf.Lerp(1f, TerrainManager.Instance.Config.MountainHardnessFactor, influence);
                    amount *= hardMult;

                    TerrainManager.Instance.ModifyHeight(bestHit.Value.point, amount, BrushRadius * radiusMult);
                }
            }
        }
    }
}
