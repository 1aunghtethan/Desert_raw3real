using System;
using UnityEngine;

/// <summary>
/// Manages the player's hotbar inventory (Minecraft-style 1-10 slot selection).
/// Attach to the Player GameObject.
/// </summary>
public class Inventory : MonoBehaviour
{
    public static Inventory Instance { get; private set; }

    [Header("Inventory")]
    [Tooltip("Item slots. Handbar (0-1) = equippable, Bag (2-9) = storage.")]
    public ItemData[] Slots = new ItemData[10];
    public int[] SlotCounts = new int[10];

    /// <summary>Number of handbar slots (scroll wheel and number keys cycle through these only).</summary>
    public int HotbarSize = 2;

    [Header("Input")]
    public KeyCode[] SlotKeys = {
        KeyCode.Alpha1, KeyCode.Alpha2
    };

    /// <summary>Currently selected slot index (0-8 for hotbar).</summary>
    public int SelectedIndex { get; private set; } = 0;

    /// <summary>Currently selected item (null = empty hand).</summary>
    public ItemData SelectedItem => (SelectedIndex >= 0 && SelectedIndex < Slots.Length) ? Slots[SelectedIndex] : null;

    /// <summary>Fired when the selected slot changes. Args: (newIndex, newItemData or null).</summary>
    public event Action<int, ItemData> OnSelectedItemChanged;

    /// <summary>Fired when ANY slot contents change (drag/drop, added, removed).</summary>
    public event Action OnInventoryChanged;

    private ItemPlacer _itemPlacer;

    public void BroadcastInventoryChange()
    {
        OnInventoryChanged?.Invoke();
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        _itemPlacer = GetComponent<ItemPlacer>();
        if (_itemPlacer == null) _itemPlacer = FindFirstObjectByType<ItemPlacer>();
    }

    void Update()
    {
        HandleHotbarInput();
    }

    private void HandleHotbarInput()
    {
        // Number keys 1-9
        for (int i = 0; i < SlotKeys.Length && i < Slots.Length; i++)
        {
            if (Input.GetKeyDown(SlotKeys[i]))
            {
                SelectSlot(i);
                return;
            }
        }

        // Scroll wheel cycling (hotbar only, slots 0 to HotbarSize-1)
        if (_itemPlacer != null && _itemPlacer.IsPlacementModeActive)
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            int hotbar = Mathf.Min(HotbarSize, Slots.Length);
            int dir = scroll > 0 ? -1 : 1;
            // Clamp current index into hotbar range before cycling
            int cur = Mathf.Clamp(SelectedIndex, 0, hotbar - 1);
            int newIndex = (cur + dir + hotbar) % hotbar;
            SelectSlot(newIndex);
        }
    }

    /// <summary>
    /// Select a specific slot by index.
    /// </summary>
    public void SelectSlot(int index, bool force = false)
    {
        if (index < 0 || index >= Slots.Length) return;
        if (!force && index == SelectedIndex) return;

        SelectedIndex = index;
        OnSelectedItemChanged?.Invoke(SelectedIndex, SelectedItem);
    }

    /// <summary>
    /// Adds an item to the first empty slot or stacks it onto existing items.
    /// Returns true if successfully added.
    /// </summary>
    public bool AddItem(ItemData item)
    {
        return AddItem(item, 1);
    }

    /// <summary>
    /// Adds multiple copies of an item, stacking first and then using empty slots.
    /// Rolls back anything added if the full amount cannot fit.
    /// </summary>
    public bool AddItem(ItemData item, int amount)
    {
        if (item == null) return false;
        if (amount <= 0) return true;

        int[] originalCounts = (int[])SlotCounts.Clone();
        ItemData[] originalSlots = (ItemData[])Slots.Clone();
        int remaining = amount;

        // 1. Try to find existing stack
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] != null && Slots[i].ItemName == item.ItemName)
            {
                int space = Mathf.Max(0, Slots[i].MaxStack - SlotCounts[i]);
                if (space > 0)
                {
                    int toAdd = Mathf.Min(space, remaining);
                    SlotCounts[i] += toAdd;
                    remaining -= toAdd;
                    if (remaining <= 0)
                    {
                        NotifyInventoryMutated();
                        return true;
                    }
                }
            }
        }

        // 2. Try to find empty slot
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] == null)
            {
                Slots[i] = item;
                int toAdd = Mathf.Min(Mathf.Max(1, item.MaxStack), remaining);
                SlotCounts[i] = toAdd;
                remaining -= toAdd;
                if (remaining <= 0)
                {
                    NotifyInventoryMutated();
                    return true;
                }
            }
        }

        Slots = originalSlots;
        SlotCounts = originalCounts;
        Debug.LogWarning($"[Inventory] Not enough space for {amount}x {item.ItemName}.");
        return false;
    }

    /// <summary>
    /// Removes one count of the item from a specific slot.
    /// If count reaches 0, the slot is cleared.
    /// </summary>
    public void RemoveItem(int slotIndex, int amount = 1)
    {
        if (amount <= 0) return;
        if (slotIndex < 0 || slotIndex >= Slots.Length) return;
        if (Slots[slotIndex] == null) return;

        SlotCounts[slotIndex] -= amount;
        if (SlotCounts[slotIndex] <= 0)
        {
            Slots[slotIndex] = null;
            SlotCounts[slotIndex] = 0;
        }

        if (slotIndex == SelectedIndex) OnSelectedItemChanged?.Invoke(SelectedIndex, SelectedItem);
        BroadcastInventoryChange();
    }

    /// <summary>
    /// Removes one count of the specified item from the inventory.
    /// </summary>
    public void RemoveItem(ItemData item, int amount = 1)
    {
        if (item == null) return;
        if (amount <= 0) return;

        int remaining = amount;
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] != null && Slots[i].ItemName == item.ItemName)
            {
                int toRemove = Mathf.Min(SlotCounts[i], remaining);
                RemoveItem(i, toRemove);
                remaining -= toRemove;
                if (remaining <= 0) return;
            }
        }
    }

    public int CountItem(ItemData item)
    {
        if (item == null) return 0;

        int count = 0;
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] != null && Slots[i].ItemName == item.ItemName)
            {
                count += SlotCounts[i];
            }
        }

        return count;
    }

    public bool HasItem(ItemData item, int amount = 1)
    {
        return CountItem(item) >= amount;
    }

    public bool HasItems(System.Collections.Generic.IEnumerable<CraftingIngredient> ingredients)
    {
        if (ingredients == null) return true;

        System.Collections.Generic.Dictionary<string, int> required = new System.Collections.Generic.Dictionary<string, int>();
        foreach (CraftingIngredient ingredient in ingredients)
        {
            if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0) continue;
            string itemName = ingredient.Item.ItemName;
            if (!required.ContainsKey(itemName)) required[itemName] = 0;
            required[itemName] += ingredient.Amount;
        }

        foreach (System.Collections.Generic.KeyValuePair<string, int> requirement in required)
        {
            if (CountItemByName(requirement.Key) < requirement.Value) return false;
        }

        return true;
    }

    public bool RemoveItems(System.Collections.Generic.IEnumerable<CraftingIngredient> ingredients)
    {
        if (!HasItems(ingredients)) return false;

        foreach (CraftingIngredient ingredient in ingredients)
        {
            if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0) continue;
            if (ingredient.PreserveAfterCraft) continue;
            RemoveItem(ingredient.Item, ingredient.Amount);
        }

        return true;
    }

    private void NotifyInventoryMutated()
    {
        OnSelectedItemChanged?.Invoke(SelectedIndex, SelectedItem);
        BroadcastInventoryChange();
    }

    private int CountItemByName(string itemName)
    {
        int count = 0;
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] != null && Slots[i].ItemName == itemName)
            {
                count += SlotCounts[i];
            }
        }

        return count;
    }
}
