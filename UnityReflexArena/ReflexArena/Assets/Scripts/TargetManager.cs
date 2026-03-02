// =============================================================================
// TargetManager.cs
// Attached to: GameManager (in Game scene)
// Purpose: Spawns and manages target UI elements during rounds.
//          Uses a shared random seed so all clients spawn targets at identical positions.
//
// NETWORKING:
//   - Server determines spawn parameters (count, seed) and broadcasts via RPC.
//   - Each client instantiates targets LOCALLY at the same positions (same seed = same Random).
//   - Each player's targets are INDEPENDENT — clicking yours doesn't affect others'.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class TargetManager : MonoBehaviour
{
    public static TargetManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("The target prefab (a UI Image) to instantiate")]
    public GameObject targetPrefab;

    [Tooltip("The PlayArea RectTransform (targets spawn inside this area)")]
    public RectTransform playArea;

    // Active targets this client can click
    private List<GameObject> activeTargets = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    /// Summary:
    /// Spawn targets at positions determined by the given seed.
    /// Because all clients use the same seed, Random.Range produces
    /// identical positions on every machine — this is how the server
    /// ensures targets appear in the same places for everyone.
    /// <param name="count">Number of targets to spawn</param>
    /// <param name="seed">Random seed from the server (ensures identical positions)</param>
    public void SpawnTargets(int count, int seed)
    {
        ClearAllTargets();

        // Use the server-provided seed so all clients get identical positions
        Random.State originalState = Random.state;
        Random.InitState(seed);

        float areaW = playArea.rect.width;
        float areaH = playArea.rect.height;
        float padding = 50f;
        float targetSize = 70f;

        // Generate non-overlapping positions
        List<Vector2> positions = new List<Vector2>();
        int maxAttempts = 200;

        for (int i = 0; i < count; i++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float x = Random.Range(-areaW/2 + padding, areaW/2 - padding);
                float y = Random.Range(-areaH/2 + padding, areaH/2 - padding);
                Vector2 candidate = new Vector2(x, y);

                // Check overlap with existing targets
                bool overlaps = false;
                foreach (var pos in positions)
                {
                    if (Vector2.Distance(candidate, pos) < targetSize + 10f) // 10px gap
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    positions.Add(candidate);
                    placed = true;
                    break;
                }
            }

            // Fallback: if we couldn't find a non-overlapping spot, place anyway
            if (!placed)
            {
                float x = Random.Range(-areaW/2 + padding, areaW/2 - padding);
                float y = Random.Range(-areaH/2 + padding, areaH/2 - padding);
                positions.Add(new Vector2(x, y));
            }
        }

        // Restore original random state (so we don't affect other random calls)
        Random.state = originalState;

        // Instantiate targets at the computed positions
        foreach (var pos in positions)
        {
            GameObject target = Instantiate(targetPrefab, playArea);
            RectTransform rt = target.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(targetSize, targetSize);
            target.SetActive(true);
            activeTargets.Add(target);
        }
    }

    /// Summary:
    /// Remove all active targets from the screen.
    public void ClearAllTargets()
    {
        foreach (var t in activeTargets)
        {
            if (t != null) Destroy(t);
        }
        activeTargets.Clear();
    }

    /// Summary:
    /// Remove a specific target (when the local player clicks it).
    public void RemoveTarget(GameObject target)
    {
        if (activeTargets.Contains(target))
        {
            activeTargets.Remove(target);
            Destroy(target);
        }
    }

    /// Summary:
    /// Get the list of currently active targets (for bot or input checking).
    public List<GameObject> GetActiveTargets() => activeTargets;
}