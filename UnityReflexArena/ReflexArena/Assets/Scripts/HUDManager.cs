// =============================================================================
// HUDManager.cs
// Attached to: GameManager (in Game scene)
// Purpose: Manages miscellaneous HUD elements and live hit counter display.
//          Subscribes to roundHits NetworkVariable for live score updates.
//
// NETWORKING:
//   - Reads NetworkVariable<ScoreData> roundHits in Update for live display.
//   - No RPCs — purely reactive to network state.
// =============================================================================

using UnityEngine;
using Unity.Netcode;
using TMPro;

public class HUDManager : NetworkBehaviour
{
    public static HUDManager Instance { get; private set; }

    [Header("Live Score Display (during round)")]
    [Tooltip("Optional: shows YOUR hit count during the round")]
    public TMP_Text liveHitText;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (NetworkGameManager.Instance == null) return;
        if (NetworkGameManager.Instance.phase.Value != NetworkGameManager.GamePhase.RoundActive) return;

        // Show this player's hit count during active round
        if (liveHitText != null)
        {
            int myIndex = NetworkGameManager.Instance.GetPlayerIndex(NetworkManager.Singleton.LocalClientId);
            if (myIndex >= 0)
            {
                int myHits = NetworkGameManager.Instance.roundHits.Value.Get(myIndex);
                liveHitText.text = $"Hits: {myHits}";
            }
        }
    }
}