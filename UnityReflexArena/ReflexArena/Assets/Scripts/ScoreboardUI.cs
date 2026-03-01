using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Attached to: GameManager (or a dedicated UI object)
// Purpose: Displays a shared scoreboard that all clients see.
//
// Summary:
// - Called from NetworkGameManager when Scores NetworkVariable changes.
// - This fulfills "shared elements visible over the network" because every client
//   renders the same server-authoritative score state.

public class ScoreboardUI : MonoBehaviour
{
    public static ScoreboardUI Instance { get; private set; }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================

    [Header("Score Text Elements (Player 1–4)")]
    public TMP_Text player1Text;
    public TMP_Text player2Text;
    public TMP_Text player3Text;
    public TMP_Text player4Text;

    [Header("Appearance")]
    [Tooltip("Tint used for empty player slots")]
    public Color emptySlotColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Tooltip("Tint used for active player slots")]
    public Color activeSlotColor = Color.white;

    private TMP_Text[] _entries;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        _entries = new[]
        {
            player1Text,
            player2Text,
            player3Text,
            player4Text
        };
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>
    /// Updates the scoreboard text for player slots 1–4.
    /// Called on every client whenever the server updates the score NetworkVariable.
    /// </summary>
    public void UpdateScoreboard(
        NetworkGameManager.ScoreData scoreData,
        Dictionary<ulong, int> playerIndexByClientId,
        int connectedPlayers
    )
    {
        // Defensive: handle missing UI wiring gracefully
        if (_entries == null || _entries.Length != 4)
        {
            Debug.LogWarning("[ScoreboardUI] Score entry references not configured.");
            return;
        }

        // Basic display: slot index is Player #.
        // NOTE: If you later want "clientId -> slot label", you can use playerIndexByClientId.
        for (int i = 0; i < 4; i++)
        {
            TMP_Text entry = _entries[i];
            if (entry == null) continue;

            bool slotActive = i < connectedPlayers;

            entry.gameObject.SetActive(true);
            entry.color = slotActive ? activeSlotColor : emptySlotColor;

            entry.text = slotActive
                ? $"Player {i + 1}: {scoreData.Get(i)}"
                : $"Player {i + 1}: ---";
        }
    }
}