using Unity.Netcode;
using UnityEngine;

// Attached to: GameManager (same object as NetworkGameManager)
// Purpose: Server-authoritative target placement and visibility.
//
// Summary:
// - Server chooses a random target position inside the play area.
// - Server broadcasts the position/visibility to all clients via RPC.
// - Clients only render what the server decides (same target placement for everyone).

public class TargetManager : NetworkBehaviour
{
    public static TargetManager Instance { get; private set; }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================

    [Header("Target")]
    [Tooltip("Target RectTransform (UI element inside the Play Area)")]
    public RectTransform targetRect;

    [Tooltip("Target GameObject that gets enabled/disabled")]
    public GameObject targetObject;

    [Header("Play Area")]
    [Tooltip("RectTransform that defines the valid target spawn bounds")]
    public RectTransform playAreaRect;

    [Header("Placement")]
    [Tooltip("Padding from the play area edges (prevents clipping)")]
    [Min(0f)]
    public float edgePaddingPx = 60f;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    // =========================================================================
    // SERVER METHODS
    // =========================================================================

    /// <summary>
    /// SERVER ONLY: Picks a random anchored position within the play area
    /// and tells all clients to show the target there.
    /// </summary>
    public void SpawnTarget()
    {
        if (!IsServer) return;

        if (playAreaRect == null || targetRect == null || targetObject == null)
        {
            Debug.LogError("[Server] TargetManager missing inspector references.");
            return;
        }

        Vector2 pos = GetRandomAnchoredPosition();
        Debug.Log($"[Server] Spawning target at ({pos.x:F0}, {pos.y:F0})");

        ShowTargetRpc(pos.x, pos.y);
    }

    /// <summary>
    /// SERVER ONLY: Hides the target on all clients.
    /// </summary>
    public void HideTarget()
    {
        if (!IsServer) return;
        HideTargetRpc();
    }

    private Vector2 GetRandomAnchoredPosition()
    {
        float halfWidth = (playAreaRect.rect.width * 0.5f) - edgePaddingPx;
        float halfHeight = (playAreaRect.rect.height * 0.5f) - edgePaddingPx;

        // If play area is too small for current padding, clamp to 0 to avoid invalid ranges.
        halfWidth = Mathf.Max(0f, halfWidth);
        halfHeight = Mathf.Max(0f, halfHeight);

        float x = Random.Range(-halfWidth, halfWidth);
        float y = Random.Range(-halfHeight, halfHeight);

        return new Vector2(x, y);
    }

    // =========================================================================
    // RPCs — SERVER -> ALL CLIENTS
    // =========================================================================

    /// <summary>
    /// [Server → All Clients] Show the target at the specified anchored position.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void ShowTargetRpc(float x, float y)
    {
        targetRect.anchoredPosition = new Vector2(x, y);
        targetObject.SetActive(true);

        Debug.Log($"[Client] Target shown at ({x:F0}, {y:F0})");
    }

    /// <summary>
    /// [Server → All Clients] Hide the target.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void HideTargetRpc()
    {
        targetObject.SetActive(false);
    }
}