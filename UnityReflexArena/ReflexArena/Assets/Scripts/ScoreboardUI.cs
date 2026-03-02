// =============================================================================
// ScoreboardUI.cs
// Attached to: GameManager (in Game scene)
// Purpose: Updates the scoreboard overlay shown BETWEEN rounds.
//          Driven by NetworkVariable OnValueChanged callbacks.
//
// NETWORKING:
//   - This script reads from NetworkVariables via callbacks.
//   - All clients see identical scoreboard because it's driven by
//     server-authoritative NetworkVariables.
// =============================================================================

using UnityEngine;
using TMPro;

public class ScoreboardUI : MonoBehaviour
{
    public static ScoreboardUI Instance { get; private set; }

    [Header("Score Text Entries")]
    public TMP_Text scoreEntry1;
    public TMP_Text scoreEntry2;
    public TMP_Text scoreEntry3;
    public TMP_Text scoreEntry4;
    public TMP_Text scoreboardTitle;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    /// Summary:
    /// Called from NetworkGameManager's OnValueChanged callback.
    /// Updates all score entries. This runs on EVERY client because
    /// the NetworkVariable change triggers it everywhere.
    public void Refresh(NetworkGameManager.ScoreData wins,
                        NetworkGameManager.ScoreData hits,
                        int connectedPlayers,
                        int currentRound)
    {
        TMP_Text[] entries = { scoreEntry1, scoreEntry2, scoreEntry3, scoreEntry4 };

        if (scoreboardTitle != null)
            scoreboardTitle.text = $"SCOREBOARD — After Round {currentRound}";

        for (int i = 0; i < 4; i++)
        {
            if (entries[i] == null) continue;

            if (i < connectedPlayers)
            {
                entries[i].text = $"Player {i+1}:  {wins.Get(i)} wins  |  {hits.Get(i)} hits this round";
                entries[i].color = Color.white;
                entries[i].gameObject.SetActive(true);
            }
            else
            {
                entries[i].text = $"Player {i+1}: ---";
                entries[i].color = new Color(0.4f, 0.4f, 0.4f);
            }
        }
    }
}