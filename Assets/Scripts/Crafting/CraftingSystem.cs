using System;
using System.Collections.Generic;
using UnityEngine;

public class CraftingSystem : MonoBehaviour
{
    public static CraftingSystem Instance { get; private set; }

    [Header("Recipes")]
    public List<CraftingRecipe> Recipes = new List<CraftingRecipe>();

    [Header("Input")]
    public KeyCode CraftSelectedKey = KeyCode.C;
    public int SelectedRecipeIndex;

    private Inventory _inventory;

    public event Action OnCraftingChanged;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _inventory = Inventory.Instance;
        if (_inventory == null) _inventory = FindFirstObjectByType<Inventory>();
        LoadResourceRecipes();

        if (_inventory != null)
        {
            _inventory.OnInventoryChanged += HandleInventoryChanged;
        }
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= HandleInventoryChanged;
        }
    }

    private void Update()
    {
        // Crafting now happens through CraftingPanelUI's ingredient/output slots.
    }

    public bool CanCraft(CraftingRecipe recipe)
    {
        if (_inventory == null || recipe == null || !recipe.IsValid) return false;
        return _inventory.HasItems(recipe.Ingredients) && CanFitResultAfterConsumingIngredients(recipe);
    }

    public bool CraftSelectedRecipe()
    {
        if (SelectedRecipeIndex < 0 || SelectedRecipeIndex >= Recipes.Count) return false;
        return Craft(Recipes[SelectedRecipeIndex]);
    }

    public bool Craft(CraftingRecipe recipe)
    {
        if (_inventory == null || recipe == null || !recipe.IsValid) return false;
        if (!_inventory.HasItems(recipe.Ingredients)) return false;

        ItemData[] originalSlots = (ItemData[])_inventory.Slots.Clone();
        int[] originalCounts = (int[])_inventory.SlotCounts.Clone();

        if (!_inventory.RemoveItems(recipe.Ingredients)) return false;

        if (_inventory.AddItem(recipe.Result, recipe.ResultAmount))
        {
            Debug.Log($"[CraftingSystem] Crafted {recipe.ResultAmount}x {recipe.Result.ItemName}.");
            OnCraftingChanged?.Invoke();
            return true;
        }

        _inventory.Slots = originalSlots;
        _inventory.SlotCounts = originalCounts;
        _inventory.SelectSlot(_inventory.SelectedIndex, true);
        _inventory.BroadcastInventoryChange();
        Debug.LogWarning($"[CraftingSystem] Inventory filled before adding crafted result: {recipe.Result.ItemName}.");
        return false;
    }

    public void SelectRecipe(int index)
    {
        SelectedRecipeIndex = Mathf.Clamp(index, 0, Mathf.Max(0, Recipes.Count - 1));
        OnCraftingChanged?.Invoke();
    }

    public string GetIngredientSummary(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.Ingredients == null) return string.Empty;

        List<string> parts = new List<string>();
        foreach (CraftingIngredient ingredient in recipe.Ingredients)
        {
            if (ingredient == null || ingredient.Item == null) continue;
            int owned = _inventory != null ? _inventory.CountItem(ingredient.Item) : 0;
            parts.Add($"{ingredient.Item.ItemName} {owned}/{ingredient.Amount}");
        }

        return string.Join(", ", parts);
    }

    private bool CanFitResultAfterConsumingIngredients(CraftingRecipe recipe)
    {
        if (_inventory == null || recipe == null || recipe.Result == null) return false;

        ItemData[] simulatedSlots = (ItemData[])_inventory.Slots.Clone();
        int[] simulatedCounts = (int[])_inventory.SlotCounts.Clone();

        foreach (CraftingIngredient ingredient in recipe.Ingredients)
        {
            if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0) continue;

            int remainingToRemove = ingredient.Amount;
            for (int i = 0; i < simulatedSlots.Length && remainingToRemove > 0; i++)
            {
                if (simulatedSlots[i] == null || simulatedSlots[i].ItemName != ingredient.Item.ItemName) continue;

                int removed = Mathf.Min(simulatedCounts[i], remainingToRemove);
                simulatedCounts[i] -= removed;
                remainingToRemove -= removed;

                if (simulatedCounts[i] <= 0)
                {
                    simulatedSlots[i] = null;
                    simulatedCounts[i] = 0;
                }
            }
        }

        int remaining = recipe.ResultAmount;
        for (int i = 0; i < simulatedSlots.Length; i++)
        {
            ItemData slotItem = simulatedSlots[i];
            if (slotItem == null)
            {
                remaining -= Mathf.Max(1, recipe.Result.MaxStack);
            }
            else if (slotItem.ItemName == recipe.Result.ItemName)
            {
                remaining -= Mathf.Max(0, slotItem.MaxStack - simulatedCounts[i]);
            }

            if (remaining <= 0) return true;
        }

        return false;
    }

    private void HandleInventoryChanged()
    {
        OnCraftingChanged?.Invoke();
    }

    private void LoadResourceRecipes()
    {
        CraftingRecipe[] loadedRecipes = Resources.LoadAll<CraftingRecipe>("CraftingRecipes");
        foreach (CraftingRecipe recipe in loadedRecipes)
        {
            if (recipe != null && !Recipes.Contains(recipe))
            {
                Recipes.Add(recipe);
            }
        }
    }
}
