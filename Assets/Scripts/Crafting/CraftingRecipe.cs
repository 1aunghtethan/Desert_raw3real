using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Sand/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    [Header("Output")]
    public ItemData Result;
    [Min(1)] public int ResultAmount = 1;

    [Header("Ingredients")]
    public List<CraftingIngredient> Ingredients = new List<CraftingIngredient>();

    public bool IsValid => Result != null && ResultAmount > 0;
}
