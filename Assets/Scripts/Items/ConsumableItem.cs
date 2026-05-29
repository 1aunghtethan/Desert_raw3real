using UnityEngine;

/// <summary>
/// Behavior for consumable items (food/drink).
/// Consumes the item from inventory and restores player stats.
/// </summary>
public class ConsumableItem : ItemBehaviour
{
    public override bool Use()
    {
        if (Data == null || IsOnCooldown()) return false;

        if (IsBlockedByHighHunger(Data))
            return false;

        EquipmentHolder holder = EquipmentHolder.Instance;
        if (Data != null && (Data.HungerRestore > 0f || Data.SaturationRestore > 0f) &&
            (holder == null || !holder.IsEatUseApplyingEffect))
        {
            holder?.PlayEatAnimation();
        }

        // Perform consumption
        if (PlayerStats.Instance != null)
        {
            PlayerStats.Instance.CurrentHealth = Mathf.Min(PlayerStats.Instance.MaxHealth, PlayerStats.Instance.CurrentHealth + Data.HealthRestore);
            PlayerStats.Instance.CurrentHunger = Mathf.Min(PlayerStats.Instance.MaxHunger, PlayerStats.Instance.CurrentHunger + Data.HungerRestore);
            PlayerStats.Instance.HungerSaturation += Data.SaturationRestore; // Add saturation
            PlayerStats.Instance.CurrentThirst = Mathf.Min(PlayerStats.Instance.MaxThirst, PlayerStats.Instance.CurrentThirst + Data.ThirstRestore);
            
            Debug.Log($"[ConsumableItem] Consumed {Data.ItemName}. Hunger: {PlayerStats.Instance.CurrentHunger}, Saturation: {PlayerStats.Instance.HungerSaturation}, Thirst: {PlayerStats.Instance.CurrentThirst}");
            
            // Remove one item from inventory
            if (Inventory.Instance != null)
            {
                Inventory.Instance.RemoveItem(Data, 1);
            }
        }

        _lastUseTime = Time.time;
        return true;
    }

    private static bool IsBlockedByHighHunger(ItemData item)
    {
        if (item == null || PlayerStats.Instance == null)
            return false;

        bool isBlockedFood = item.ItemName == "Beef" || item.ItemName == "Died Cat";
        if (!isBlockedFood)
            return false;

        if (PlayerStats.Instance.CurrentHunger < PlayerStats.Instance.MaxHunger * 0.8f)
            return false;

        Debug.Log($"[ConsumableItem] {item.ItemName} cannot be eaten while hunger is above 80%.");
        return true;
    }
}
