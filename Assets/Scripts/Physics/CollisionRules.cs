using UnityEngine;

/// <summary>
/// Sets up global collision rules for the game.
/// Ensures the Player and Items (Meat) do not collide with each other.
/// </summary>
public class CollisionRules : MonoBehaviour
{
    private const string PlayerLayerName = "Player";
    private const string ItemLayerName = "Item";

    private void Awake()
    {
        int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
        int itemLayer = LayerMask.NameToLayer(ItemLayerName);

        if (playerLayer != -1 && itemLayer != -1)
        {
            Physics.IgnoreLayerCollision(playerLayer, itemLayer, true);
            Debug.Log($"[CollisionRules] Layer collision ignored between '{PlayerLayerName}' and '{ItemLayerName}'");
        }
        else
        {
            Debug.LogWarning($"[CollisionRules] Could not find layers: {PlayerLayerName}({playerLayer}), {ItemLayerName}({itemLayer})");
            return;
        }

        GameObject player = GameObject.FindWithTag(PlayerLayerName);
        if (player == null)
            player = GameObject.Find("player") ?? GameObject.Find("Player");

        if (player != null)
        {
            SetLayerRecursively(player, playerLayer);
        }

        try
        {
            foreach (GameObject item in GameObject.FindGameObjectsWithTag("Pickable"))
                SetLayerRecursively(item, itemLayer);
        }
        catch (UnityException)
        {
            Debug.LogWarning("[CollisionRules] Tag 'Pickable' is not defined; skipping pickable item layer setup.");
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;

        foreach (Transform child in target.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
