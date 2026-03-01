using Unity.Netcode;
using UnityEngine;

// Attached to: GameManager (or a dedicated local-only object)
// Purpose: Client input -> send click data to server.
//
// Summary:
// - Runs only on clients/host.
// - On click during RoundActive, computes pixel distance from click to target center.
// - Sends distance to server via NetworkGameManager.PlayerClickRpc().

public class PlayerNetworkController : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Target RectTransform (UI element the player clicks)")]
    public RectTransform targetRectTransform;

    [Tooltip("Canvas containing the target (Screen Space Overlay recommended)")]
    public Canvas gameCanvas;

    private void Update()
    {
        if (!IsSpawned) return;
        if (!IsClient) return;

        if (Input.GetMouseButtonDown(0))
        {
            TrySendClick();
        }
    }

    private void TrySendClick()
    {
        if (NetworkGameManager.Instance == null) return;

        // Gate input locally so we only send one click per round
        if (!NetworkGameManager.Instance.TryLocalClickGate()) return;

        // Target must be active to measure distance
        if (targetRectTransform == null) return;
        if (!targetRectTransform.gameObject.activeInHierarchy) return;

        Vector2 clickScreenPos = Input.mousePosition;

        // Correct screen position for UI element
        Vector2 targetScreenPos = RectTransformUtility.WorldToScreenPoint(
            gameCanvas != null ? gameCanvas.worldCamera : null,
            targetRectTransform.position
        );

        float distancePx = Vector2.Distance(clickScreenPos, targetScreenPos);

        Debug.Log($"[Client] Click sent. Distance={distancePx:F1}px");
        NetworkGameManager.Instance.PlayerClickRpc(distancePx);
    }
}