using UnityEngine;

/// <summary>
/// Sets up global collision rules for the game.
/// Ensures the Player and Items (Meat) do not collide with each other.
/// </summary>
public class CollisionRules : MonoBehaviour
{
    private void Awake()
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        int itemLayer = LayerMask.NameToLayer("Item");

        if (playerLayer != -1 && itemLayer != -1)
        {
            Physics.IgnoreLayerCollision(playerLayer, itemLayer, true);
            Debug.Log($"[CollisionRules] Layer collision ignored between '{playerLayer}' and '{itemLayer}'");
        }
        else
        {
            Debug.LogWarning($"[CollisionRules] Could not find layers: Player({playerLayer}), Item({itemLayer})");
        }

        // Ensure Player object is actually on the Player layer
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null && playerLayer != -1)
        {
            player.layer = playerLayer;
            // Also set children (like camera/body) if needed, but usually the root collider is enough
        }
    }
}
