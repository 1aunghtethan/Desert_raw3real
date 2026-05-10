using UnityEngine;

/// <summary>
/// Manages the physical weapon model in the player's hand.
/// Listens to Inventory selection changes and instantiates/destroys weapon prefabs.
/// Attach to the Player GameObject.
/// </summary>
public class EquipmentHolder : MonoBehaviour
{
    public static EquipmentHolder Instance { get; private set; }

    [Header("References")]
    [Tooltip("Empty child GameObject where weapons are attached. Created automatically if null.")]
    public Transform HandAnchor;

    [Tooltip("Offset from the camera for first-person weapon positioning (Right, Up, Forward).")]
    public Vector3 FPHandOffset = new Vector3(0.7f, -0.5f, 0.9f);

    [Tooltip("Offset from the player body for third-person positioning.")]
    public Vector3 TPHandOffset = new Vector3(0.5f, 1.2f, 0.5f);

    [Header("Minecraft Style Animation")]
    public float SwayAmount = 2f;
    public float SwaySmoothness = 10f;
    public float BobSpeed = 12f;
    public float BobAmount = 0.05f;

    private float _bobTimer;

    private Inventory _inventory;
    private PlayerController _player;
    private GameObject _currentWeaponObj;
    private ItemBehaviour _currentBehaviour;

    /// <summary>The currently equipped item behaviour (null if empty hand).</summary>
    public ItemBehaviour CurrentBehaviour => _currentBehaviour;
    public ItemData CurrentItem => _inventory != null ? _inventory.SelectedItem : null;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        _inventory = GetComponent<Inventory>();
        _player = GetComponent<PlayerController>();

        if (_inventory == null)
        {
            Debug.LogError("[EquipmentHolder] Requires Inventory on same GameObject!");
            return;
        }

        // Create hand anchor if not assigned
        if (HandAnchor == null)
        {
            GameObject anchor = new GameObject("HandAnchor");
            anchor.transform.SetParent(transform);
            anchor.transform.localPosition = new Vector3(0.5f, 1.2f, 0.5f);
            HandAnchor = anchor.transform;
        }

        // Subscribe to inventory changes
        _inventory.OnSelectedItemChanged += OnSlotChanged;

        // Equip initial item
        EquipItem(_inventory.SelectedItem);
    }

    void OnDestroy()
    {
        if (_inventory != null)
            _inventory.OnSelectedItemChanged -= OnSlotChanged;
    }

    void Update()
    {
        HandleInput();
        UpdateHandPosition();
    }

    private void HandleInput()
    {
        if (_currentBehaviour == null) return;

        // Left-click = primary use (attack/consume)
        if (Input.GetMouseButtonDown(0))
        {
            _currentBehaviour.Use();
        }

        // Right-click = secondary use (only for weapons with AltUse)
        // NOTE: ItemPlacer handles right-click for non-weapon items (Tool/Consumable)
        // NOTE: ItemDropper handles Q-key dropping separately
        if (Input.GetMouseButtonDown(1))
        {
            _currentBehaviour?.AltUse();
        }
    }

    private void UpdateHandPosition()
    {
        if (HandAnchor == null || _player == null) return;

        Camera cam = _player.GetActiveCamera();
        if (cam == null) return;

        if (_player.CurrentMode == PlayerController.CameraMode.FirstPerson)
        {
            // FP mode: parent to camera so it stays exactly with us
            if (HandAnchor.parent != cam.transform)
            {
                HandAnchor.SetParent(cam.transform, false);
            }

            Vector3 targetPos = FPHandOffset;
            Quaternion targetRot = Quaternion.identity;

            // Apply Bobbing based on movement input
            float moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")).magnitude;
            if (moveInput > 0.1f)
            {
                _bobTimer += Time.deltaTime * BobSpeed;
                float bobY = Mathf.Sin(_bobTimer) * BobAmount;
                float bobX = Mathf.Cos(_bobTimer * 0.5f) * BobAmount;
                targetPos += new Vector3(bobX, bobY, 0);
            }
            else
            {
                // Smoothly return to center
                _bobTimer = 0f;
            }

            // Apply Sway based on mouse movement (inverted for natural lag)
            float mouseX = -Input.GetAxis("Mouse X") * SwayAmount;
            float mouseY = Input.GetAxis("Mouse Y") * SwayAmount;
            
            targetRot = Quaternion.Euler(mouseY, mouseX, 0); // Local rotation lag

            // Apply smoothing
            HandAnchor.localPosition = Vector3.Lerp(HandAnchor.localPosition, targetPos, Time.deltaTime * 15f);
            HandAnchor.localRotation = Quaternion.Slerp(HandAnchor.localRotation, targetRot, Time.deltaTime * SwaySmoothness);
        }
        else
        {
            // TP mode: parent to player body
            if (HandAnchor.parent != transform)
            {
                HandAnchor.SetParent(transform, false);
            }
            
            HandAnchor.localPosition = TPHandOffset;
            HandAnchor.localRotation = Quaternion.identity;
        }

        // Apply item's hold settings dynamically so they can be tweaked in the Inspector live
        if (_currentWeaponObj != null && CurrentItem != null)
        {
            // NaN Protection
            Vector3 pos = CurrentItem.HoldPosition;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) pos = Vector3.zero;
            
            _currentWeaponObj.transform.localPosition = pos;
            _currentWeaponObj.transform.localRotation = Quaternion.Euler(CurrentItem.HoldRotation);
            
            // Allow manual scale tweaking. We use Lerp to smooth out slider dragging
            float targetScale = CurrentItem.HoldScale;
            if (float.IsNaN(targetScale)) targetScale = 1f;

            _currentWeaponObj.transform.localScale = Vector3.Lerp(_currentWeaponObj.transform.localScale, Vector3.one * targetScale, Time.deltaTime * 15f);
        }
    }

    private void OnSlotChanged(int index, ItemData item)
    {
        EquipItem(item);
    }

    private void EquipItem(ItemData item)
    {
        // Cleanup old weapon
        if (_currentBehaviour != null)
        {
            _currentBehaviour.OnUnequip();
        }
        if (_currentWeaponObj != null)
        {
            Destroy(_currentWeaponObj);
            _currentWeaponObj = null;
            _currentBehaviour = null;
        }

        if (item == null || item.Prefab == null) return;

        // Spawn weapon
        try
        {
            _currentWeaponObj = Instantiate(item.Prefab, HandAnchor);
        }
        catch (System.InvalidCastException)
        {
            Debug.LogWarning($"[Equipment] {item.ItemName} has an invalid hand prefab reference. Leaving hand empty.");
            return;
        }

        _currentWeaponObj.transform.localPosition = item.HoldPosition;
        _currentWeaponObj.transform.localRotation = Quaternion.Euler(item.HoldRotation);
        _currentWeaponObj.transform.localScale = Vector3.one * item.HoldScale;

        // Get or add appropriate behaviour
        _currentBehaviour = _currentWeaponObj.GetComponent<ItemBehaviour>();
        if (_currentBehaviour == null)
        {
            // Auto-add based on ItemType
            switch (item.Type)
            {
                case ItemType.Melee:
                    _currentBehaviour = _currentWeaponObj.AddComponent<MeleeWeapon>();
                    break;
                case ItemType.Ranged:
                    _currentBehaviour = _currentWeaponObj.AddComponent<RangedWeapon>();
                    break;
                case ItemType.Consumable:
                    _currentBehaviour = _currentWeaponObj.AddComponent<ConsumableItem>();
                    break;
                default:
                    _currentBehaviour = _currentWeaponObj.AddComponent<ItemBehaviour>();
                    break;
            }
        }

        // Initialize
        Camera cam = (_player != null) ? _player.GetActiveCamera() : Camera.main;
        _currentBehaviour.OnEquip(item, transform, cam);

        // Disable physics on the held weapon
        Rigidbody rb = _currentWeaponObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Disable colliders on held weapon so it doesn't bump the player
        foreach (var col in _currentWeaponObj.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        // Clean up any world-drop components that might be on the prefab
        // so it doesn't try to hover or show pickup outlines in the player's hand!
        var loot = _currentWeaponObj.GetComponent<LootItem>();
        if (loot != null) Destroy(loot);
        
        var outline = _currentWeaponObj.GetComponent<OutlineController>();
        if (outline != null) Destroy(outline);
        
        var wSpin = _currentWeaponObj.GetComponent<WorldItemSpin>();
        if (wSpin != null) Destroy(wSpin);

        Debug.Log($"[Equipment] Equipped: {item.ItemName}");
    }
}
