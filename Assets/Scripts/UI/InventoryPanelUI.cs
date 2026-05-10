using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Place this script on the main Inventory UI Canvas or Panel in your Scene.
/// Requires child UI elements with InventorySlotUI attached.
/// </summary>
public class InventoryPanelUI : MonoBehaviour
{
    [Header("Behavior")]
    public KeyCode ToggleKey = KeyCode.Tab;
    public bool PauseGameWhenOpen = false;

    [Header("Manual Setup")]
    [Tooltip("Drag your InventorySlotUI objects here in order (0 to 8). If left empty, it will auto-find them.")]
    public InventorySlotUI[] Slots;

    private CanvasGroup _canvasGroup;
    private Inventory _inventory;
    private PlayerController _playerController;
    private EquipmentHolder _equipmentHolder;
    private ThirdPersonCamera _thirdPersonCamera;
    private MonoBehaviour _sandInteraction;
    private HotbarController _hotbarController;

    private bool _isOpen = false;

    // Drag and Drop
    private ItemData _heldItem;
    private int _heldCount;
    private GameObject _cursorIconObj;
    private Image _cursorIconImage;
    private Text _cursorIconText;

    public bool HasHeldItem => _heldItem != null && _heldCount > 0;
    public ItemData HeldItem => _heldItem;
    public int HeldCount => _heldCount;

    private void Awake()
    {
        // Immediately hide the panel before anything renders
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
        _isOpen = false;
    }

    private void Start()
    {
        
        // Ensure the parent Canvas has a GraphicRaycaster (required for UI click events)
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null && parentCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
        {
            parentCanvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        // Ensure an EventSystem exists in the scene
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        _inventory = Inventory.Instance;
        if (_inventory == null)
            _inventory = FindFirstObjectByType<Inventory>();

        // Cache player references to disable input when inventory is open
        _playerController = FindFirstObjectByType<PlayerController>();
        _equipmentHolder = FindFirstObjectByType<EquipmentHolder>();
        _thirdPersonCamera = FindFirstObjectByType<ThirdPersonCamera>();

        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb.GetType().Name == "SandInteraction")
            {
                _sandInteraction = mb;
                break;
            }
        }

        // AUTO-SETUP: Find the container that holds the slot children
        // This works with Grid Layout Groups, Vertical/Horizontal Layout Groups, or any parent with children
        SetupSlots();

        CreateCursorIcon();

        if (_inventory != null)
        {
            _inventory.OnSelectedItemChanged += OnSelectionChanged;
            _inventory.OnInventoryChanged += OnInventoryDataChanged;
        }

        SetVisible(false);
    }

    /// <summary>
    /// Finds all child GameObjects across all LayoutGroups and attaches InventorySlotUI to each one.
    /// If the inventory has more slots than the grids contain, a Backpack section is auto-created.
    /// </summary>
    private void SetupSlots()
    {
        // If user manually assigned slots, use those
        if (Slots != null && Slots.Length > 0)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                ConfigureSlot(Slots[i], i);
            }
            Debug.Log($"[InventoryPanelUI] Using {Slots.Length} manually assigned slots.");
            return;
        }

        int totalInventorySlots = (_inventory != null) ? _inventory.Slots.Length : 15;
        int hotbarSize = (_inventory != null) ? _inventory.HotbarSize : 9;

        // Collect slots from GridLayoutGroups only (not Vertical/Horizontal which are containers)
        var slotList = new System.Collections.Generic.List<InventorySlotUI>();
        GridLayoutGroup[] allGrids = GetComponentsInChildren<GridLayoutGroup>(true);

        foreach (GridLayoutGroup grid in allGrids)
        {
            Transform container = grid.transform;
            for (int c = 0; c < container.childCount && slotList.Count < totalInventorySlots; c++)
            {
                Transform child = container.GetChild(c);
                // Skip children that are themselves layout groups (they're containers, not slots)
                if (child.GetComponent<LayoutGroup>() != null) continue;

                InventorySlotUI slot = child.GetComponent<InventorySlotUI>();
                if (slot == null)
                {
                    slot = child.gameObject.AddComponent<InventorySlotUI>();
                }
                int idx = slotList.Count;
                ConfigureSlot(slot, idx);
                slotList.Add(slot);
            }
        }

        // If no layout groups found, try direct children
        if (slotList.Count == 0)
        {
            Transform slotContainer = transform;
            for (int c = 0; c < transform.childCount; c++)
            {
                Transform child = transform.GetChild(c);
                if (child.childCount > 0)
                {
                    slotContainer = child;
                    break;
                }
            }
            for (int c = 0; c < slotContainer.childCount && slotList.Count < totalInventorySlots; c++)
            {
                Transform child = slotContainer.GetChild(c);
                InventorySlotUI slot = child.GetComponent<InventorySlotUI>();
                if (slot == null) slot = child.gameObject.AddComponent<InventorySlotUI>();
                ConfigureSlot(slot, slotList.Count);
                slotList.Add(slot);
            }
        }

        // AUTO-CREATE: If we found fewer slots than the inventory needs, create a Backpack grid
        if (slotList.Count < totalInventorySlots)
        {
            int needed = totalInventorySlots - slotList.Count;
            Debug.Log($"[InventoryPanelUI] Creating Backpack grid for {needed} extra slots.");

            // Find the parent of the existing grid to add the backpack section alongside it
            Transform panelParent = transform;
            if (allGrids.Length > 0)
            {
                panelParent = allGrids[0].transform.parent != null ? allGrids[0].transform.parent : transform;
            }

            // Create "Backpack" label
            GameObject labelObj = new GameObject("BackpackLabel");
            labelObj.transform.SetParent(panelParent, false);
            RectTransform labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(300, 25);
            Text labelText = labelObj.AddComponent<Text>();
            labelText.text = "Backpack";
            // Font defaults to Arial/LegacyRuntime automatically
            labelText.fontSize = 16;
            labelText.fontStyle = FontStyle.Bold;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
            labelText.raycastTarget = false;

            // Create the backpack grid container
            GameObject backpackObj = new GameObject("BackpackGrid");
            backpackObj.transform.SetParent(panelParent, false);
            RectTransform bpRect = backpackObj.AddComponent<RectTransform>();
            bpRect.sizeDelta = new Vector2(350, 200);
            GridLayoutGroup bpGrid = backpackObj.AddComponent<GridLayoutGroup>();

            // Match existing grid settings if possible
            if (allGrids.Length > 0)
            {
                bpGrid.cellSize = allGrids[0].cellSize;
                bpGrid.spacing = allGrids[0].spacing;
                bpGrid.padding = allGrids[0].padding;
                bpGrid.childAlignment = allGrids[0].childAlignment;
                bpGrid.constraint = allGrids[0].constraint;
                bpGrid.constraintCount = allGrids[0].constraintCount;
            }
            else
            {
                bpGrid.cellSize = new Vector2(70, 70);
                bpGrid.spacing = new Vector2(5, 5);
                bpGrid.childAlignment = TextAnchor.UpperCenter;
            }

            // Create slot children in the backpack grid
            for (int i = 0; i < needed; i++)
            {
                GameObject slotObj = new GameObject($"BackpackSlot_{i}");
                slotObj.transform.SetParent(backpackObj.transform, false);
                Image slotBg = slotObj.AddComponent<Image>();
                slotBg.color = new Color(0.25f, 0.2f, 0.15f, 0.7f); // Slightly different color for backpack

                InventorySlotUI slot = slotObj.AddComponent<InventorySlotUI>();
                int idx = slotList.Count;
                ConfigureSlot(slot, idx);
                slotList.Add(slot);
            }
        }

        if (slotList.Count == 0)
        {
            Debug.LogWarning("[InventoryPanelUI] No slot children found! Make sure this panel has child GameObjects for each slot.");
        }

        Slots = slotList.ToArray();
        Debug.Log($"[InventoryPanelUI] Auto-setup {Slots.Length} slots (hotbar={Mathf.Min(hotbarSize, Slots.Length)}, backpack={Mathf.Max(0, Slots.Length - hotbarSize)}).");
    }

    private void ConfigureSlot(InventorySlotUI slot, int index)
    {
        slot.SlotIndex = index;
        slot.ParentPanel = this;

        // Ensure the slot GameObject can receive clicks (needs a Graphic with raycastTarget)
        Image slotImage = slot.GetComponent<Image>();
        if (slotImage == null)
        {
            slotImage = slot.gameObject.AddComponent<Image>();
            slotImage.color = new Color(0.2f, 0.2f, 0.2f, 0.6f);
        }
        slotImage.raycastTarget = true;

        // Try to find the icon image (a child Image that isn't the slot background)
        if (slot.IconImage == null)
        {
            Image[] childImages = slot.GetComponentsInChildren<Image>(true);
            foreach (Image img in childImages)
            {
                if (img.gameObject != slot.gameObject)
                {
                    slot.IconImage = img;
                    break;
                }
            }
        }

        // AUTO-CREATE icon Image if none found
        if (slot.IconImage == null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(slot.transform, false);
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(6, 6);
            iconRect.offsetMax = new Vector2(-6, -6);
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.color = new Color(1f, 1f, 1f, 0f); // Start transparent
            slot.IconImage = iconImg;
        }

        // Try to find text components
        if (slot.NameText == null || slot.CountText == null)
        {
            Text[] texts = slot.GetComponentsInChildren<Text>(true);
            foreach (Text t in texts)
            {
                if (slot.NameText == null)
                {
                    slot.NameText = t;
                }
                else if (slot.CountText == null)
                {
                    slot.CountText = t;
                }
            }
        }

        // AUTO-CREATE NameText if none found
        if (slot.NameText == null)
        {
            GameObject nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(slot.transform, false);
            RectTransform nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            Text nameText = nameObj.AddComponent<Text>();
            // Font defaults automatically
            nameText.fontSize = 12;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.color = Color.white;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Outline nameOutline = nameObj.AddComponent<Outline>();
            nameOutline.effectColor = Color.black;
            slot.NameText = nameText;
        }

        // AUTO-CREATE CountText if none found
        if (slot.CountText == null)
        {
            GameObject countObj = new GameObject("CountText");
            countObj.transform.SetParent(slot.transform, false);
            RectTransform countRect = countObj.AddComponent<RectTransform>();
            countRect.anchorMin = new Vector2(1, 0);
            countRect.anchorMax = new Vector2(1, 0);
            countRect.pivot = new Vector2(1, 0);
            countRect.sizeDelta = new Vector2(52, 32);
            countRect.anchoredPosition = new Vector2(-4, 4);
            Text countText = countObj.AddComponent<Text>();
            countText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            countText.fontSize = 25;
            countText.fontStyle = FontStyle.Bold;
            countText.alignment = TextAnchor.LowerRight;
            countText.color = Color.white;
            countText.raycastTarget = false;
            Outline countOutline = countObj.AddComponent<Outline>();
            countOutline.effectColor = Color.black;
            slot.CountText = countText;
        }
        else
        {
            slot.CountText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            slot.CountText.fontSize = 25;
            slot.CountText.raycastTarget = false;
        }

        slot.CountText.transform.SetAsLastSibling();

        // CRITICAL: Disable raycastTarget on ALL child graphics so they don't steal clicks
        // Only the slot's own Image should receive clicks (which triggers OnPointerClick)
        Graphic[] allChildGraphics = slot.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic g in allChildGraphics)
        {
            if (g.gameObject != slot.gameObject)
            {
                g.raycastTarget = false;
            }
        }
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.OnSelectedItemChanged -= OnSelectionChanged;
            _inventory.OnInventoryChanged -= OnInventoryDataChanged;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
        {
            ToggleInventory();
        }

        if (_isOpen && _heldItem != null && _cursorIconObj != null)
        {
            _cursorIconObj.transform.position = Input.mousePosition;
        }
    }

    private void CreateCursorIcon()
    {
        _cursorIconObj = new GameObject("CursorIcon");
        _cursorIconObj.transform.SetParent(transform, false);
        _cursorIconObj.transform.SetAsLastSibling(); // Make sure it renders on top
        
        RectTransform rt = _cursorIconObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(50, 50);
        
        _cursorIconImage = _cursorIconObj.AddComponent<Image>();
        _cursorIconImage.raycastTarget = false;
        
        GameObject textObj = new GameObject("Count");
        textObj.transform.SetParent(_cursorIconObj.transform, false);
        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = new Vector2(1, 0);
        textRt.anchorMax = new Vector2(1, 0);
        textRt.pivot = new Vector2(1, 0);
        textRt.anchoredPosition = new Vector2(0, 0);
        textRt.sizeDelta = new Vector2(40, 20);
        
        _cursorIconText = textObj.AddComponent<Text>();
        // Font defaults automatically
        _cursorIconText.fontSize = 16;
        _cursorIconText.alignment = TextAnchor.LowerRight;
        _cursorIconText.raycastTarget = false;
        _cursorIconText.color = Color.white;

        Outline outline = textObj.AddComponent<Outline>();
        outline.effectColor = Color.black;

        _cursorIconObj.SetActive(false);
    }

    public void ToggleInventory()
    {
        SetVisible(!_isOpen);
    }

    public void SetVisible(bool visible)
    {
        CraftingPanelUI craftingPanel = FindFirstObjectByType<CraftingPanelUI>(FindObjectsInactive.Include);
        if (craftingPanel != null)
        {
            craftingPanel.SetVisible(visible);
        }

        _isOpen = visible;
        
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        if (visible)
        {
            RefreshAll();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Disable player controls so the world doesn't respond
            if (_playerController != null) _playerController.enabled = false;
            if (_equipmentHolder != null) _equipmentHolder.enabled = false;
            if (_thirdPersonCamera != null) _thirdPersonCamera.enabled = false;
            if (_sandInteraction != null) _sandInteraction.enabled = false;

            // Hide hotbar so it doesn't steal clicks from inventory slots
            if (_hotbarController == null) _hotbarController = FindFirstObjectByType<HotbarController>();
            if (_hotbarController != null) _hotbarController.gameObject.SetActive(false);

            if (PauseGameWhenOpen) Time.timeScale = 0f;
        }
        else
        {
            if (craftingPanel != null)
            {
                craftingPanel.ReturnIngredientsToInventory();
            }

            // If we closed the inventory while holding something, throw it back into the first empty slot
            if (_heldItem != null)
            {
                for (int i = 0; i < _inventory.Slots.Length; i++)
                {
                    if (_inventory.Slots[i] == null)
                    {
                        _inventory.Slots[i] = _heldItem;
                        _inventory.SlotCounts[i] = _heldCount;
                        break;
                    }
                }
                
                _heldItem = null;
                _heldCount = 0;
                UpdateCursorIcon();
                _inventory.BroadcastInventoryChange();
            }

            // Re-enable player controls
            if (_playerController != null) _playerController.enabled = true;
            if (_equipmentHolder != null) _equipmentHolder.enabled = true;
            if (_thirdPersonCamera != null) _thirdPersonCamera.enabled = true;
            if (_sandInteraction != null) _sandInteraction.enabled = true;

            // Show hotbar again
            if (_hotbarController != null) _hotbarController.gameObject.SetActive(true);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (PauseGameWhenOpen) Time.timeScale = 1f;
        }
    }

    public bool TryRemoveOneHeldItem()
    {
        if (_heldItem == null || _heldCount <= 0) return false;

        _heldCount--;
        if (_heldCount <= 0)
        {
            _heldItem = null;
            _heldCount = 0;
        }

        UpdateCursorIcon();
        return true;
    }

    public bool TrySetHeldItem(ItemData item, int count)
    {
        if (item == null || count <= 0) return false;
        if (_heldItem != null) return false;

        _heldItem = item;
        _heldCount = count;
        UpdateCursorIcon();
        return true;
    }

    public bool CanAcceptHeldItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;
        if (_heldItem == null) return true;
        if (_heldItem.ItemName != item.ItemName) return false;
        return _heldCount + amount <= Mathf.Max(1, item.MaxStack);
    }

    public bool AddToHeldItem(ItemData item, int amount)
    {
        if (!CanAcceptHeldItem(item, amount)) return false;

        if (_heldItem == null)
        {
            _heldItem = item;
            _heldCount = amount;
        }
        else
        {
            _heldCount += amount;
        }

        UpdateCursorIcon();
        return true;
    }

    public void OnSlotClicked(int index)
    {
        if (_inventory == null) return;
        if (index < 0 || index >= _inventory.Slots.Length) return;
        
        ItemData clickedItem = _inventory.Slots[index];
        int clickedCount = _inventory.SlotCounts[index];

        if (_heldItem == null)
        {
            // Hand is empty
            if (clickedItem != null)
            {
                // Pick up item
                _heldItem = clickedItem;
                _heldCount = clickedCount;
                
                _inventory.Slots[index] = null;
                _inventory.SlotCounts[index] = 0;
            }
            else
            {
                // Clicked an empty slot with an empty hand
                int hotbarSize = (_inventory != null) ? _inventory.HotbarSize : 9;
                if (index < hotbarSize)
                {
                    // Hotbar slot: close menu and equip this slot
                    _inventory.SelectSlot(index);
                    SetVisible(false);
                }
                // Backpack slot: do nothing (can't equip backpack slots directly)
                return;
            }
        }
        else
        {
            // Hand is holding an item
            if (clickedItem == null)
            {
                // Drop into empty slot
                _inventory.Slots[index] = _heldItem;
                _inventory.SlotCounts[index] = _heldCount;
                
                _heldItem = null;
                _heldCount = 0;
            }
            else if (clickedItem.ItemName == _heldItem.ItemName && clickedCount < clickedItem.MaxStack)
            {
                // Stack items
                int space = clickedItem.MaxStack - clickedCount;
                int transferAmount = Mathf.Min(space, _heldCount);
                
                _inventory.SlotCounts[index] += transferAmount;
                _heldCount -= transferAmount;
                
                if (_heldCount <= 0)
                {
                    _heldItem = null;
                    _heldCount = 0;
                }
            }
            else
            {
                // Swap items
                ItemData tempItem = clickedItem;
                int tempCount = clickedCount;
                
                _inventory.Slots[index] = _heldItem;
                _inventory.SlotCounts[index] = _heldCount;
                
                _heldItem = tempItem;
                _heldCount = tempCount;
            }
        }

        UpdateCursorIcon();
        RefreshAll();
        
        // If we modified the slot that the player is currently holding, tell the system to refresh the physical weapon Model
        if (index == _inventory.SelectedIndex)
        {
            _inventory.SelectSlot(index, true); // force re-equip to spawn new model
        }
        
        _inventory.BroadcastInventoryChange();
    }

    private void UpdateCursorIcon()
    {
        if (_heldItem != null && _cursorIconObj != null)
        {
            _cursorIconObj.SetActive(true);
            _cursorIconImage.sprite = _heldItem.Icon;
            _cursorIconText.text = _heldCount > 1 ? _heldCount.ToString() : "";
            // Immediately snap to mouse so it doesn't flicker at 0,0
            _cursorIconObj.transform.position = Input.mousePosition; 
        }
        else if (_cursorIconObj != null)
        {
            _cursorIconObj.SetActive(false);
        }
    }

    private void OnSelectionChanged(int index, ItemData item)
    {
        if (_isOpen) RefreshAll();
    }

    private void OnInventoryDataChanged()
    {
        if (_isOpen) RefreshAll();
    }

    private void RefreshAll()
    {
        if (_inventory == null || Slots == null) return;

        for (int i = 0; i < Slots.Length; i++)
        {
            if (i < _inventory.Slots.Length && Slots[i] != null)
            {
                ItemData item = _inventory.Slots[i];
                int count = _inventory.SlotCounts[i];
                bool isSelected = (i == _inventory.SelectedIndex);
                
                Slots[i].Refresh(item, count, isSelected);
            }
        }
    }
}
