using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CraftingPanelUI : MonoBehaviour
{
    private const int IngredientSlotCount = 3;
    private const int OutputSlotIndex = 3;
    private const int CraftingSlotCapacity = 999;

    [Header("Behavior")]
    public bool PauseGameWhenOpen;

    [Header("References")]
    public CraftingSystem Crafting;
    public Transform RecipeListRoot;

    private readonly ItemData[] _items = new ItemData[IngredientSlotCount];
    private readonly int[] _counts = new int[IngredientSlotCount];
    private readonly List<CraftingSlotUI> _slots = new List<CraftingSlotUI>();

    private CanvasGroup _canvasGroup;
    private Inventory _inventory;
    private InventoryPanelUI _inventoryPanel;
    private CraftingRecipe _currentRecipe;
    private bool _isOpen;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        SetVisible(false);
    }

    private void Start()
    {
        if (Crafting == null) Crafting = CraftingSystem.Instance;
        if (Crafting == null) Crafting = FindFirstObjectByType<CraftingSystem>();
        if (RecipeListRoot == null) RecipeListRoot = transform;

        _inventory = Inventory.Instance;
        if (_inventory == null) _inventory = FindFirstObjectByType<Inventory>();
        _inventoryPanel = FindFirstObjectByType<InventoryPanelUI>(FindObjectsInactive.Include);

        EnsureEventSystem();
        BuildSlots();
        Refresh();
    }

    public void SetVisible(bool visible)
    {
        _isOpen = visible;
        if (_canvasGroup == null) return;

        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.interactable = visible;
        _canvasGroup.blocksRaycasts = visible;

        if (PauseGameWhenOpen)
        {
            Time.timeScale = visible ? 0f : 1f;
        }

        if (visible)
        {
            Refresh();
        }
    }

    public void OnCraftingSlotClicked(int slotIndex, PointerEventData.InputButton button = PointerEventData.InputButton.Left)
    {
        if (!_isOpen) return;
        if (_inventoryPanel == null) _inventoryPanel = FindFirstObjectByType<InventoryPanelUI>(FindObjectsInactive.Include);

        if (slotIndex >= 0 && slotIndex < IngredientSlotCount)
        {
            HandleIngredientSlotClicked(slotIndex, button);
            Refresh();
            return;
        }

        if (slotIndex == OutputSlotIndex)
        {
            CraftCurrentRecipe();
            Refresh();
        }
    }

    public void ReturnIngredientsToInventory()
    {
        if (_inventory == null) return;

        for (int i = 0; i < IngredientSlotCount; i++)
        {
            if (_items[i] != null && _counts[i] > 0)
            {
                _inventory.AddItem(_items[i], _counts[i]);
                _items[i] = null;
                _counts[i] = 0;
            }
        }

        Refresh();
    }

    private void HandleIngredientSlotClicked(int slotIndex, PointerEventData.InputButton button)
    {
        if (_inventoryPanel == null) return;

        bool isRight = button == PointerEventData.InputButton.Right;

        if (_inventoryPanel.HasHeldItem)
        {
            ItemData held = _inventoryPanel.HeldItem;
            int heldCount = _inventoryPanel.HeldCount;
            if (held == null || heldCount <= 0) return;

            bool emptySlot = _items[slotIndex] == null;
            bool sameItem = _items[slotIndex] != null && _items[slotIndex].ItemName == held.ItemName;
            if (!emptySlot && !sameItem)
            {
                ItemData slotItem = _items[slotIndex];
                int slotCount = _counts[slotIndex];
                if (_inventoryPanel.TryReplaceHeldItem(slotItem, slotCount))
                {
                    _items[slotIndex] = held;
                    _counts[slotIndex] = heldCount;
                }
                return;
            }
            if (!emptySlot && _counts[slotIndex] >= CraftingSlotCapacity) return;

            int placeAmount;
            if (isRight)
                placeAmount = Mathf.CeilToInt(heldCount / 2f);
            else
                placeAmount = heldCount;

            int space = CraftingSlotCapacity - (emptySlot ? 0 : _counts[slotIndex]);
            placeAmount = Mathf.Min(placeAmount, space);

            if (_inventoryPanel.TryRemoveHeldAmount(placeAmount))
            {
                _items[slotIndex] = held;
                _counts[slotIndex] += placeAmount;
            }

            return;
        }

        if (_items[slotIndex] != null && _counts[slotIndex] > 0)
        {
            if (_inventoryPanel.TrySetHeldItem(_items[slotIndex], _counts[slotIndex]))
            {
                _items[slotIndex] = null;
                _counts[slotIndex] = 0;
            }
        }
    }

    private void CraftCurrentRecipe()
    {
        if (_currentRecipe == null || _inventoryPanel == null) return;
        if (!HasIngredientsFor(_currentRecipe)) return;
        ItemData result = _currentRecipe.Result;
        if (result == null) return;

        // Single-ingredient transformation: replace in-slot instead of cursor
        if (result.DirectPlaceOnCraft)
        {
            ItemPlacer placer = _inventory != null ? _inventory.GetComponent<ItemPlacer>() : null;
            if (placer == null) placer = FindFirstObjectByType<ItemPlacer>();
            if (placer == null || !placer.CanStartDirectPlacement(result)) return;

            foreach (CraftingIngredient ingredient in _currentRecipe.Ingredients)
            {
                if (ingredient.TransformAfterCraft != null)
                    TransformIngredientSlot(ingredient, ingredient.TransformAfterCraft);
                else if (!ingredient.PreserveAfterCraft)
                    ConsumeIngredient(ingredient);
            }

            AudioManager.Instance.PlayCraftSound();
            _inventoryPanel.SetVisible(false);
            placer.BeginDirectPlacement(result);
        }
        else if (_currentRecipe.Ingredients.Count == 1 && !_currentRecipe.Ingredients[0].PreserveAfterCraft)
        {
            for (int i = 0; i < IngredientSlotCount; i++)
            {
                if (_items[i] != null && _items[i].ItemName == _currentRecipe.Ingredients[0].Item.ItemName)
                {
                    _items[i] = result;
                    _counts[i] = _currentRecipe.ResultAmount;
                    AudioManager.Instance.PlayCraftSound();
                    break;
                }
            }
        }
        else
        {
            if (!_inventoryPanel.CanAcceptHeldItem(result, _currentRecipe.ResultAmount)) return;

            foreach (CraftingIngredient ingredient in _currentRecipe.Ingredients)
            {
                if (ingredient.TransformAfterCraft != null)
                    TransformIngredientSlot(ingredient, ingredient.TransformAfterCraft);
                else if (!ingredient.PreserveAfterCraft)
                    ConsumeIngredient(ingredient);
            }

            _inventoryPanel.AddToHeldItem(result, _currentRecipe.ResultAmount);
            AudioManager.Instance.PlayCraftSound();
        }
    }

    private void TransformIngredientSlot(CraftingIngredient ingredient, ItemData newItem)
    {
        for (int i = 0; i < IngredientSlotCount; i++)
        {
            if (_items[i] != null && _items[i].ItemName == ingredient.Item.ItemName)
            {
                _items[i] = newItem;
                _counts[i] = 1;
                break;
            }
        }
    }

    private void ConsumeIngredient(CraftingIngredient ingredient)
    {
        int remaining = ingredient.Amount;
        for (int i = 0; i < IngredientSlotCount && remaining > 0; i++)
        {
            if (_items[i] == null || _items[i].ItemName != ingredient.Item.ItemName) continue;

            int removed = Mathf.Min(_counts[i], remaining);
            _counts[i] -= removed;
            remaining -= removed;

            if (_counts[i] <= 0)
            {
                _items[i] = null;
                _counts[i] = 0;
            }
        }
    }

    private void BuildSlots()
    {
        _slots.Clear();
        if (RecipeListRoot == null) return;

        CraftingSlotUI[] existingSlots = RecipeListRoot.GetComponentsInChildren<CraftingSlotUI>(true);
        foreach (CraftingSlotUI existingSlot in existingSlots)
        {
            if (_slots.Count >= 4) break;
            _slots.Add(ConfigureExistingSlot(existingSlot.gameObject, _slots.Count));
        }

        if (_slots.Count < 4)
        {
            Image[] childImages = RecipeListRoot.GetComponentsInChildren<Image>(true);
            foreach (Image image in childImages)
            {
                if (_slots.Count >= 4) break;
                if (image.transform == RecipeListRoot) continue;
                if (image.GetComponent<CraftingSlotUI>() != null) continue;
                if (image.GetComponentInParent<CraftingSlotUI>() != null) continue;
                if (image.transform.childCount > 0 && image.GetComponentsInChildren<Image>(true).Length > 1) continue;

                _slots.Add(ConfigureExistingSlot(image.gameObject, _slots.Count));
            }
        }

        for (int i = _slots.Count; i < 4; i++)
        {
            _slots.Add(CreateSlot(i));
        }
    }

    private CraftingSlotUI ConfigureExistingSlot(GameObject slotObj, int index)
    {
        Image background = slotObj.GetComponent<Image>();
        if (background == null) background = slotObj.AddComponent<Image>();
        background.raycastTarget = true;

        CraftingSlotUI slot = slotObj.GetComponent<CraftingSlotUI>();
        if (slot == null) slot = slotObj.AddComponent<CraftingSlotUI>();

        slot.ParentPanel = this;
        slot.SlotIndex = index;
        ConfigureSlotGraphics(slot, slotObj.transform);
        return slot;
    }

    private CraftingSlotUI CreateSlot(int index)
    {
        GameObject slotObj = new GameObject(index == OutputSlotIndex ? "Recipe_4_Output" : $"Recipe_{index + 1}_Ingredient");
        slotObj.transform.SetParent(RecipeListRoot, false);

        RectTransform rect = slotObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(76f, 76f);

        Image background = slotObj.AddComponent<Image>();
        background.color = index == OutputSlotIndex
            ? new Color(0.3f, 0.23f, 0.11f, 0.95f)
            : new Color(0.18f, 0.16f, 0.13f, 0.95f);
        background.raycastTarget = true;

        CraftingSlotUI slot = slotObj.AddComponent<CraftingSlotUI>();
        slot.ParentPanel = this;
        slot.SlotIndex = index;
        ConfigureSlotGraphics(slot, slotObj.transform);
        return slot;
    }

    private void ConfigureSlotGraphics(CraftingSlotUI slot, Transform parent)
    {
        Transform existingIcon = parent.Find("Icon");
        GameObject iconObj = existingIcon != null ? existingIcon.gameObject : new GameObject("Icon");
        if (existingIcon == null) iconObj.transform.SetParent(parent, false);
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        if (iconRect == null) iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(8f, 8f);
        iconRect.offsetMax = new Vector2(-8f, -8f);
        Image icon = iconObj.GetComponent<Image>();
        if (icon == null) icon = iconObj.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        slot.IconImage = icon;

        Transform existingName = parent.Find("NameText");
        GameObject nameObj = existingName != null ? existingName.gameObject : new GameObject("NameText");
        if (existingName == null) nameObj.transform.SetParent(parent, false);
        RectTransform nameRect = nameObj.GetComponent<RectTransform>();
        if (nameRect == null) nameRect = nameObj.AddComponent<RectTransform>();
        nameRect.anchorMin = Vector2.zero;
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = new Vector2(4f, 4f);
        nameRect.offsetMax = new Vector2(-4f, -4f);
        Text nameText = nameObj.GetComponent<Text>();
        if (nameText == null) nameText = nameObj.AddComponent<Text>();
        nameText.fontSize = 11;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.color = Color.white;
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        nameText.raycastTarget = false;
        Outline nameOutline = nameObj.GetComponent<Outline>();
        if (nameOutline == null) nameOutline = nameObj.AddComponent<Outline>();
        nameOutline.effectColor = Color.black;
        slot.NameText = nameText;

        Transform existingCount = parent.Find("CountText");
        GameObject countObj = existingCount != null ? existingCount.gameObject : new GameObject("CountText");
        if (existingCount == null) countObj.transform.SetParent(parent, false);
        RectTransform countRect = countObj.GetComponent<RectTransform>();
        if (countRect == null) countRect = countObj.AddComponent<RectTransform>();
        countRect.anchorMin = new Vector2(1f, 0f);
        countRect.anchorMax = new Vector2(1f, 0f);
        countRect.pivot = new Vector2(1f, 0f);
        countRect.anchoredPosition = new Vector2(-4f, 4f);
        countRect.sizeDelta = new Vector2(52f, 32f);
        Text countText = countObj.GetComponent<Text>();
        if (countText == null) countText = countObj.AddComponent<Text>();
        countText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        countText.fontSize = 25;
        countText.fontStyle = FontStyle.Bold;
        countText.alignment = TextAnchor.LowerRight;
        countText.color = Color.white;
        countText.raycastTarget = false;
        Outline countOutline = countObj.GetComponent<Outline>();
        if (countOutline == null) countOutline = countObj.AddComponent<Outline>();
        countOutline.effectColor = Color.black;
        slot.CountText = countText;
        slot.CountText.transform.SetAsLastSibling();
    }

    private void Refresh()
    {
        _currentRecipe = FindMatchingRecipe();

        for (int i = 0; i < _slots.Count; i++)
        {
            if (i < IngredientSlotCount)
            {
                _slots[i].Refresh(_items[i], _counts[i]);
            }
            else
            {
                ItemData result = _currentRecipe != null ? _currentRecipe.Result : null;
                int resultAmount = _currentRecipe != null ? _currentRecipe.ResultAmount : 0;
                _slots[i].Refresh(result, resultAmount);
            }
        }
    }

    private CraftingRecipe FindMatchingRecipe()
    {
        if (Crafting == null || Crafting.Recipes == null) return null;

        foreach (CraftingRecipe recipe in Crafting.Recipes)
        {
            if (recipe != null && recipe.IsValid && HasIngredientsFor(recipe))
            {
                return recipe;
            }
        }

        return null;
    }

    private bool HasIngredientsFor(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.Ingredients == null) return false;

        Dictionary<string, int> required = new Dictionary<string, int>();
        foreach (CraftingIngredient ingredient in recipe.Ingredients)
        {
            if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0) continue;
            string itemName = ingredient.Item.ItemName;
            if (!required.ContainsKey(itemName)) required[itemName] = 0;
            required[itemName] += ingredient.Amount;
        }

        Dictionary<string, int> provided = new Dictionary<string, int>();
        for (int i = 0; i < IngredientSlotCount; i++)
        {
            if (_items[i] == null || _counts[i] <= 0) continue;
            string itemName = _items[i].ItemName;
            if (!provided.ContainsKey(itemName)) provided[itemName] = 0;
            provided[itemName] += _counts[i];
        }

        foreach (KeyValuePair<string, int> item in provided)
        {
            if (!required.ContainsKey(item.Key)) return false;
        }

        foreach (KeyValuePair<string, int> requirement in required)
        {
            if (!provided.ContainsKey(requirement.Key) || provided[requirement.Key] < requirement.Value) return false;
        }

        return true;
    }

    private void ConsumeIngredients(CraftingRecipe recipe)
    {
        foreach (CraftingIngredient ingredient in recipe.Ingredients)
        {
            if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0) continue;
            if (ingredient.PreserveAfterCraft) continue;

            int remaining = ingredient.Amount;
            for (int i = 0; i < IngredientSlotCount && remaining > 0; i++)
            {
                if (_items[i] == null || _items[i].ItemName != ingredient.Item.ItemName) continue;

                int removed = Mathf.Min(_counts[i], remaining);
                _counts[i] -= removed;
                remaining -= removed;

                if (_counts[i] <= 0)
                {
                    _items[i] = null;
                    _counts[i] = 0;
                }
            }
        }
    }

    private void EnsureEventSystem()
    {
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null && parentCanvas.GetComponent<GraphicRaycaster>() == null)
        {
            parentCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }
    }
}
