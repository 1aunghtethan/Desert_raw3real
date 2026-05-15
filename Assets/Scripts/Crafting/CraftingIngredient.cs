using System;
using UnityEngine;

[Serializable]
public class CraftingIngredient
{
    public ItemData Item;
    [Min(1)] public int Amount = 1;
    public bool PreserveAfterCraft;
    public ItemData TransformAfterCraft;
}
