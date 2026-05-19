using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects to a manually-placed hotbar canvas in the hierarchy.
/// Finds slot children under "handbar" and "bag" containers and updates their icons.
/// Attach this script to the "hotbar place" GameObject in your Scene.
/// </summary>
public class HotbarController : MonoBehaviour
{
    [Header("Slot Containers (auto-found by name if empty)")]
    [Tooltip("Parent of handbar slots. Found by name 'handbar' if not set.")]
    public Transform HandbarContainer;
    [Tooltip("Parent of bag slots. Found by name 'bag' if not set.")]
    public Transform BagContainer;

    [Header("Appearance")]
    public Color NormalColor = new Color(0.2f, 0.2f, 0.2f, 0.7f);
    public Color SelectedColor = new Color(0.9f, 0.7f, 0.2f, 0.9f);

    private Inventory _inventory;

    // All slot UI elements in order: handbar first, then bag
    private SlotUI[] _slots;

    private struct SlotUI
    {
        public int InventoryIndex;
        public Image Background;
        public Image Icon;
        public Text CountText;
        public Text NameText;
    }

    void Start()
    {
        _inventory = Inventory.Instance;
        if (_inventory == null)
            _inventory = FindFirstObjectByType<Inventory>();

        // Auto-find containers by name if not assigned
        if (HandbarContainer == null)
            HandbarContainer = transform.Find("handbar");
        if (BagContainer == null)
            BagContainer = transform.Find("bag");

        SetupSlots();

        if (_inventory != null)
        {
            _inventory.OnSelectedItemChanged += OnSelectionChanged;
            _inventory.OnInventoryChanged += RefreshAll;
        }

        RefreshAll();
    }

    void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.OnSelectedItemChanged -= OnSelectionChanged;
            _inventory.OnInventoryChanged -= RefreshAll;
        }
    }

    private void SetupSlots()
    {
        var slotList = new System.Collections.Generic.List<SlotUI>();
        int index = 0;

        // Handbar slots (indices 0, 1)
        if (HandbarContainer != null)
        {
            for (int i = 0; i < HandbarContainer.childCount; i++)
            {
                slotList.Add(CreateSlotUI(HandbarContainer.GetChild(i), index));
                index++;
            }
        }

        // Bag slots (indices 2, 3, 4)
        if (BagContainer != null)
        {
            for (int i = 0; i < BagContainer.childCount; i++)
            {
                slotList.Add(CreateSlotUI(BagContainer.GetChild(i), index));
                index++;
            }
        }

        _slots = slotList.ToArray();
        Debug.Log($"[HotbarController] Set up {_slots.Length} slots (handbar={HandbarContainer?.childCount ?? 0}, bag={BagContainer?.childCount ?? 0}).");
    }

    private SlotUI CreateSlotUI(Transform slotTransform, int inventoryIndex)
    {
        SlotUI slot = new SlotUI();
        slot.InventoryIndex = inventoryIndex;

        // Get or add background Image on the slot itself
        slot.Background = slotTransform.GetComponent<Image>();
        if (slot.Background == null)
        {
            slot.Background = slotTransform.gameObject.AddComponent<Image>();
            slot.Background.color = NormalColor;
        }
        ClearSlotSourceImage(slot.Background);

        // Find or create icon Image child
        Transform iconChild = slotTransform.Find("Icon");
        if (iconChild != null)
        {
            slot.Icon = iconChild.GetComponent<Image>();
        }
        if (slot.Icon == null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(slotTransform, false);
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(6, 6);
            iconRect.offsetMax = new Vector2(-6, -6);
            slot.Icon = iconObj.AddComponent<Image>();
            slot.Icon.preserveAspect = true;
            slot.Icon.raycastTarget = false;
        }

        // Find or create count Text child
        Transform countChild = slotTransform.Find("CountText");
        if (countChild != null)
        {
            slot.CountText = countChild.GetComponent<Text>();
        }
        if (slot.CountText == null)
        {
            GameObject countObj = new GameObject("CountText");
            countObj.transform.SetParent(slotTransform, false);
            RectTransform countRect = countObj.AddComponent<RectTransform>();
            countRect.anchorMin = new Vector2(1, 0);
            countRect.anchorMax = new Vector2(1, 0);
            countRect.pivot = new Vector2(1, 0);
            countRect.sizeDelta = new Vector2(52, 32);
            countRect.anchoredPosition = new Vector2(-4, 4);
            slot.CountText = countObj.AddComponent<Text>();
            slot.CountText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            slot.CountText.fontSize = 25;
            slot.CountText.fontStyle = FontStyle.Bold;
            slot.CountText.alignment = TextAnchor.LowerRight;
            slot.CountText.color = Color.white;
            slot.CountText.raycastTarget = false;
            Outline countOutline = countObj.AddComponent<Outline>();
            countOutline.effectColor = Color.black;
        }
        else
        {
            slot.CountText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            slot.CountText.fontSize = 25;
            slot.CountText.raycastTarget = false;
        }

        slot.CountText.transform.SetAsLastSibling();

        // Find or create name Text child (for items without icons)
        Transform nameChild = slotTransform.Find("NameText");
        if (nameChild != null)
        {
            slot.NameText = nameChild.GetComponent<Text>();
        }
        if (slot.NameText == null)
        {
            GameObject nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(slotTransform, false);
            RectTransform nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
            slot.NameText = nameObj.AddComponent<Text>();
            // Font defaults to Arial/LegacyRuntime automatically
            slot.NameText.fontSize = 11;
            slot.NameText.alignment = TextAnchor.MiddleCenter;
            slot.NameText.color = Color.white;
            slot.NameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            slot.NameText.raycastTarget = false;
            Outline nameOutline = nameObj.AddComponent<Outline>();
            nameOutline.effectColor = Color.black;
        }

        return slot;
    }

    private void ClearSlotSourceImage(Image slotImage)
    {
        if (slotImage == null)
            return;

        slotImage.sprite = null;
        slotImage.overrideSprite = null;
        slotImage.type = Image.Type.Simple;
    }

    private void OnSelectionChanged(int index, ItemData item)
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_inventory == null || _slots == null) return;

        for (int i = 0; i < _slots.Length; i++)
        {
            SlotUI slot = _slots[i];
            int idx = slot.InventoryIndex;
            if (idx < 0 || idx >= _inventory.Slots.Length) continue;

            ItemData item = _inventory.Slots[idx];
            int count = _inventory.SlotCounts[idx];

            ClearSlotSourceImage(slot.Background);

            // Update icon
            if (item != null && item.Icon != null)
            {
                slot.Icon.sprite = item.Icon;
                slot.Icon.color = Color.white;
                slot.Icon.gameObject.SetActive(true);
                if (slot.NameText != null) slot.NameText.text = "";
            }
            else if (item != null)
            {
                slot.Icon.sprite = null;
                slot.Icon.color = new Color(1f, 1f, 1f, 0f);
                slot.Icon.gameObject.SetActive(false);
                if (slot.NameText != null) slot.NameText.text = item.ItemName;
            }
            else
            {
                slot.Icon.sprite = null;
                slot.Icon.color = new Color(1f, 1f, 1f, 0f);
                slot.Icon.gameObject.SetActive(false);
                if (slot.NameText != null) slot.NameText.text = "";
            }

            // Update count
            if (slot.CountText != null)
            {
                slot.CountText.text = (item != null && count > 1) ? count.ToString() : "";
                slot.CountText.gameObject.SetActive(item != null && count > 1);
                slot.CountText.transform.SetAsLastSibling();
            }

            // Update selection highlight (only for handbar slots)
            if (idx < _inventory.HotbarSize)
            {
                slot.Background.color = (idx == _inventory.SelectedIndex) ? SelectedColor : NormalColor;
            }
        }
    }
}
