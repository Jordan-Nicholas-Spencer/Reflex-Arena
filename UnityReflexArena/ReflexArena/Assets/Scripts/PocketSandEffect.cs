// =============================================================================
// PocketSandEffect.cs
// Attached to: GameManager (in Game scene)
// Purpose: Displays sand splotches on this client's screen when hit by
//          an opponent's pocket sand ability.
//
// NETWORKING:
//   - Activated by [Rpc(SendTo.ClientsAndHost)] from NetworkGameManager.
//   - Only affects the local client's display (visual-only, no NetworkVariables).
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PocketSandEffect : MonoBehaviour
{
    public static PocketSandEffect Instance { get; private set; }

    [Header("Sand Overlay")]
    public GameObject sandOverlay;       // PocketSandOverlay panel
    public Image[] splotches = new Image[3]; // Splotch1, Splotch2, Splotch3

    [Header("Tuning")]
    [Tooltip("Time in seconds for sand to fade away")]
    public float fadeDuration = 1.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    /// Summary:
    /// Called when this client is hit by pocket sand.
    /// Shows 3 splotches that fade out over 1.5 seconds.
    /// Raycast Target is OFF on these images so the player can still click through them.
    public void ActivateSand()
    {
        StartCoroutine(SandRoutine());
    }

    private IEnumerator SandRoutine()
    {
        sandOverlay.SetActive(true);

        // Set splotches to full opacity
        Color sandColor = new Color(0.71f, 0.59f, 0.35f, 0.8f);
        foreach (var s in splotches)
        {
            if (s != null)
            {
                s.color = sandColor;
                s.raycastTarget = false; // CRITICAL: let clicks pass through
            }
        }

        // Fade out over 1.5 seconds
        float fadeTime = fadeDuration;
        float elapsed = 0f;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0.8f, 0f, elapsed / fadeTime);

            foreach (var s in splotches)
            {
                if (s != null)
                    s.color = new Color(s.color.r, s.color.g, s.color.b, alpha);
            }
            yield return null;
        }

        sandOverlay.SetActive(false);
    }
}