// =============================================================================
// BotPlayer.cs
// Attached to: GameManager (in Game scene)
// Purpose: AI bot for single-player mode. Clicks targets at random intervals.
//          Only runs on the server/host.
//
// NETWORKING:
//   - Runs only when IsServer is true.
//   - Directly calls server methods (no RPCs needed — it IS the server).
//   - Removes targets from its own "virtual" target list (doesn't affect
//     the human player's targets, since targets are independent per player).
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class BotPlayer : NetworkBehaviour
{
    public static BotPlayer Instance { get; private set; }

    [Header("Bot Settings")]
    [Tooltip("Minimum time between bot clicks (seconds)")]
    public float minClickDelay = 0.3f;

    [Tooltip("Maximum time between bot clicks (seconds)")]
    public float maxClickDelay = 0.9f;

    [Tooltip("Chance the bot misses a target (0-1)")]
    public float missChance = 0.15f;

    private Coroutine botRoutine;
    private int remainingTargets = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    /// Summary:
    /// Start the bot's shooting routine for a new round.
    public void StartBotRound(int targetCount)
    {
        if (!IsServer) return;
        remainingTargets = targetCount;
        if (botRoutine != null) StopCoroutine(botRoutine);
        botRoutine = StartCoroutine(BotShootRoutine());
    }

    private IEnumerator BotShootRoutine()
    {
        // Small initial delay (bot "reacts")
        yield return new WaitForSeconds(Random.Range(0.4f, 0.8f));

        while (remainingTargets > 0)
        {
            if (NetworkGameManager.Instance.phase.Value != NetworkGameManager.GamePhase.RoundActive)
                yield break;

            // Random delay between shots
            yield return new WaitForSeconds(Random.Range(minClickDelay, maxClickDelay));

            // Bot might miss
            if (Random.value > missChance)
            {
                // Bot "hits" — register with server directly
                NetworkGameManager.Instance.RegisterBotHit();
            }

            remainingTargets--;
        }
    }

    public void StopBot()
    {
        if (botRoutine != null)
        {
            StopCoroutine(botRoutine);
            botRoutine = null;
        }
    }
}