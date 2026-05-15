using UnityEngine;

/// <summary>
/// Attach to world items to make them collectible by the player.
/// Minecraft-style: falls with gravity, settles on ground, bobs gently.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LootItem : MonoBehaviour
{
    [Header("Settings")]
    public ItemData Data;
    public float PickupRadius = 4.0f;
    public float AttractionSpeed = 30f;

    [Header("Ground Settle (Minecraft-style)")]
    public float HoverHeight = 0.25f;       // How high above ground to float
    public float BobAmplitude = 0.1f;        // Up/down bob distance
    public float BobSpeed = 2f;              // Bob speed
    public float RotateSpeed = 40f;          // Slow Y rotation like Minecraft
    public float GroundCheckDist = 50f;      // Raycast distance to find ground
    public LayerMask GroundLayers = ~0;      // Which layers count as ground

    [HideInInspector] public bool IsPlaced = false; // Set by ItemPlacer — skips hover/bob
    [HideInInspector] public ItemData SunExposedOverride; // Set by SunlightItemTransformer — used on pickup instead of Data

    private Transform m_PlayerTransform;
    private bool m_IsBeingPickedUp;
    private bool m_IsGrounded;
    private float m_GroundY;
    private float m_BobTimer;
    private Rigidbody m_Rb;

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) player = FindFirstObjectByType<PlayerController>()?.gameObject;
        if (player != null) m_PlayerTransform = player.transform;

        m_Rb = GetComponent<Rigidbody>();

        // Exclude the item's own layer from ground checks to avoid self-hit
        GroundLayers = ~(1 << gameObject.layer);
    }

    private void Update()
    {
        if (m_IsBeingPickedUp)
        {
            UpdatePickupMovement();
            return;
        }

        // Placed items stay exactly where they were put — no hover/bob
        if (IsPlaced) return;

        // If this is a heavy physics object, do not override its physical falling!
        if (Data != null && !Data.PickupsSpinAndBob) return;

        // Check for ground beneath us
        if (!m_IsGrounded)
        {
            CheckForGround();
        }

        // Once grounded, do Minecraft bob + rotate
        if (m_IsGrounded)
        {
            m_BobTimer += Time.deltaTime * BobSpeed;
            float bobOffset = Mathf.Sin(m_BobTimer) * BobAmplitude;
            transform.position = new Vector3(
                transform.position.x,
                m_GroundY + HoverHeight + bobOffset,
                transform.position.z
            );

            // Slow rotation like Minecraft
            transform.Rotate(Vector3.up, RotateSpeed * Time.deltaTime, Space.World);
        }
        else
        {
            // Safety: if item falls way below the world, snap it to terrain
            if (TerrainManager.Instance != null && transform.position.y < -500f)
            {
                float h = TerrainManager.Instance.SampleHeight(new Vector3(transform.position.x, 0, transform.position.z));
                if (!float.IsNaN(h))
                {
                    transform.position = new Vector3(transform.position.x, h + HoverHeight, transform.position.z);
                    SettleOnGround(h);
                }
            }
        }
    }

    private void CheckForGround()
    {
        // Raycast down to find the ground surface
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, GroundCheckDist, GroundLayers))
        {
            float distToGround = transform.position.y - hit.point.y;

            // If we're close to the ground (within 0.5m) or below it, settle
            if (distToGround <= 0.5f)
            {
                SettleOnGround(hit.point.y);
            }
        }
    }

    private void SettleOnGround(float groundY)
    {
        m_IsGrounded = true;
        m_GroundY = groundY;

        // Kill physics — item is now settled
        if (m_Rb != null)
        {
            m_Rb.linearVelocity = Vector3.zero;
            m_Rb.angularVelocity = Vector3.zero;
            m_Rb.isKinematic = true;
            m_Rb.useGravity = false;
        }

        // Snap to ground + hover (Skip hover for heavy static objects)
        float hover = (Data != null && !Data.PickupsSpinAndBob) ? 0f : HoverHeight;
        transform.position = new Vector3(transform.position.x, m_GroundY + hover, transform.position.z);
    }

    // Also settle when physically hitting something (collision backup)
    private void OnCollisionEnter(Collision collision)
    {
        if (m_IsGrounded || m_IsBeingPickedUp) return;
        if (Data != null && !Data.PickupsSpinAndBob) return; // Heavy physics objects stay dynamic

        // Check if we hit something below us (ground-like)
        foreach (var contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f) // Surface is mostly facing up = ground
            {
                SettleOnGround(contact.point.y);
                return;
            }
        }
    }

    private void UpdatePickupMovement()
    {
        if (m_PlayerTransform == null) return;

        float step = AttractionSpeed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, m_PlayerTransform.position + Vector3.up * 0.5f, step);

        AttractionSpeed += Time.deltaTime * 100f;

        float dist = Vector3.Distance(transform.position, m_PlayerTransform.position);
        if (dist < 1.0f)
        {
            Collect();
        }
    }

    public void RequestPickup()
    {
        if (m_IsBeingPickedUp) return;

        m_IsBeingPickedUp = true;
        m_IsGrounded = false; // Allow movement again
        Debug.Log($"[LootItem] Pickup requested for {Data?.ItemName}");

        if (m_Rb != null) m_Rb.isKinematic = true;

        if (m_PlayerTransform == null)
        {
            m_PlayerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (m_PlayerTransform == null) m_PlayerTransform = FindFirstObjectByType<PlayerController>()?.transform;
        }
    }

    private void Collect()
    {
        if (Inventory.Instance == null)
        {
            Debug.LogError("[LootItem] Inventory instance not found!");
            return;
        }

        if (Data == null)
        {
            Debug.LogError("[LootItem] ItemData is missing!");
            return;
        }

        ItemData itemToCollect = SunExposedOverride != null ? SunExposedOverride : Data;

        if (Inventory.Instance.AddItem(itemToCollect))
        {
            Debug.Log($"[LootItem] SUCCESS: Collected {itemToCollect.ItemName}");
            Destroy(gameObject);
        }
        else
        {
            Debug.LogWarning($"[LootItem] FAILED: Inventory full for {Data.ItemName}");
            m_IsBeingPickedUp = false;

            // Re-enable physics and drop it
            if (m_Rb != null)
            {
                m_Rb.isKinematic = false;
                m_Rb.useGravity = true;
            }
            m_IsGrounded = false;

            transform.position += (transform.position - m_PlayerTransform.position).normalized * 1.5f;
            AttractionSpeed = 30f;
        }
    }
}
