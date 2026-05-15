using UnityEngine;

/// <summary>
/// Replaces the currently selected inventory stack with another item when used.
/// </summary>
public class TransformSelectedItemOnUse : ItemBehaviour
{
    public ItemData ResultItem;

    public override bool Use()
    {
        if (!base.Use())
            return false;

        Inventory inventory = Inventory.Instance;
        if (inventory == null || ResultItem == null)
            return false;

        int slot = inventory.SelectedIndex;
        if (slot < 0 || slot >= inventory.Slots.Length)
            return false;

        if (inventory.Slots[slot] == null || Data == null || inventory.Slots[slot].ItemName != Data.ItemName)
            return false;

        // Apply stat restoration before transforming
        if (PlayerStats.Instance != null)
        {
            PlayerStats.Instance.CurrentHealth = Mathf.Min(PlayerStats.Instance.MaxHealth, PlayerStats.Instance.CurrentHealth + Data.HealthRestore);
            PlayerStats.Instance.CurrentHunger = Mathf.Min(PlayerStats.Instance.MaxHunger, PlayerStats.Instance.CurrentHunger + Data.HungerRestore);
            PlayerStats.Instance.HungerSaturation += Data.SaturationRestore;
            PlayerStats.Instance.CurrentThirst = Mathf.Min(PlayerStats.Instance.MaxThirst, PlayerStats.Instance.CurrentThirst + Data.ThirstRestore);
        }

        inventory.Slots[slot] = ResultItem;
        inventory.SelectSlot(slot, true);
        inventory.BroadcastInventoryChange();
        return true;
    }
}
