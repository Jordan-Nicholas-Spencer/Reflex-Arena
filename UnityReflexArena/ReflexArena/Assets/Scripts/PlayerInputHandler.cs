// =============================================================================
// PlayerInputHandler.cs
// Attached to: GameManager (in Game scene)
// Purpose: Handles all player input (shooting, reloading, jam, sand).
//          Uses the NEW Input System via UnityEngine.InputSystem.
//
// NETWORKING:
//   - Client detects input locally
//   - Sends actions to server via [Rpc(SendTo.Server)] calls on NetworkGameManager
//   - Local effects (ammo display, reload animation) happen immediately on client
//   - Server validates and updates NetworkVariables for shared state (hit counts)
//
// AMMO SYSTEM:
//   - 6 bullets. Left-click shoots (costs 1 bullet).
//   - Right-click or clicking with 0 bullets triggers reload (1.5s).
//   - Clicking during reload interrupts it — fires if ammo available.
//
// GUN JAM:
//   - Spacebar sends jam request to server (once per match).
//   - When jammed, must click to clear (1.5s).
//
// POCKET SAND:
//   - Left Shift sends sand request to server (once per match).
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Netcode;
using TMPro;

public class PlayerInputHandler : NetworkBehaviour
{
    public static PlayerInputHandler Instance { get; private set; }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    [Header("Ammo UI")]
    [Tooltip("The 6 bullet Image objects (white rectangles)")]
    public Image[] bulletImages = new Image[6]; // Assign Bullet1..Bullet6

    [Tooltip("Jam ability indicator (red circle)")]
    public Image jamIndicator;

    [Tooltip("Sand ability indicator (tan circle)")]
    public Image sandIndicator;

    [Tooltip("Text showing RELOADING / JAM status")]
    public TMP_Text reloadText;
    public GameObject reloadTextObject; // The GameObject to enable/disable

    [Header("Tuning")]
    [Tooltip("Time in seconds to reload all 6 bullets")]
    public float reloadTime = 1.5f;

    [Tooltip("Time in seconds to clear a gun jam")]
    public float jamClearTime = 1.5f;

    // =========================================================================
    // LOCAL STATE (per-client, not networked)
    // =========================================================================
    private int currentBullets = 6;
    private const int maxBullets = 6;
    private bool isReloading = false;
    private bool isJammed = false;
    private bool hasJamAbility = true;   // Once per match
    private bool hasSandAbility = true;  // Once per match
    private Coroutine reloadCoroutine;
    private Coroutine jamCoroutine;

    // Input actions (New Input System)
    private Mouse mouse;
    private Keyboard keyboard;

    // =========================================================================
    // AWAKE / NETWORK SPAWN
    // =========================================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        mouse = Mouse.current;
        keyboard = Keyboard.current;
        UpdateAmmoUI();
        UpdateAbilityUI();
    }

    // =========================================================================
    // UPDATE — Process Input Every Frame (New Input System)
    //
    // Only processes input if:
    //   1. We are a client (IsClient or IsHost)
    //   2. The game is in the RoundActive phase
    // =========================================================================
    private void Update()
    {
        if (!IsSpawned || !IsClient) return;
        if (NetworkGameManager.Instance == null) return;
        if (NetworkGameManager.Instance.phase.Value != NetworkGameManager.GamePhase.RoundActive) return;

        if (mouse == null) mouse = Mouse.current;
        if (keyboard == null) keyboard = Keyboard.current;
        if (mouse == null || keyboard == null) return;

        // --- LEFT CLICK: Shoot ---
        if (mouse.leftButton.wasPressedThisFrame)
        {
            HandleShoot();
        }

        // --- RIGHT CLICK: Reload ---
        if (mouse.rightButton.wasPressedThisFrame)
        {
            HandleReload();
        }

        // --- SPACEBAR: Jam opponent's gun ---
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            HandleJam();
        }

        // --- LEFT SHIFT: Pocket Sand ---
        if (keyboard.leftShiftKey.wasPressedThisFrame)
        {
            HandleSand();
        }
    }

    // =========================================================================
    // SHOOT LOGIC
    // =========================================================================

    private void HandleShoot()
    {
        // If jammed, start clearing the jam
        if (isJammed)
        {
            StartClearJam();
            return;
        }

        // If reloading, interrupt reload — fire if we have ammo
        if (isReloading)
        {
            StopReload();
            if (currentBullets <= 0) return;
        }

        // No ammo — do nothing on left click, must right click to reload
        if (currentBullets <= 0)
        {
            return;
        }

        // FIRE! Spend a bullet
        currentBullets--;
        UpdateAmmoUI();

        // Check if we clicked a target
        TryHitTarget();

        // If that was the last bullet, immediately show reload prompt
        if (currentBullets <= 0)
        {
            reloadTextObject.SetActive(true);
            reloadText.text = "RIGHT CLICK TO RELOAD!";
            reloadText.color = new Color(1f, 0.4f, 0.4f);
        }
    }

    /// Summary:
    /// Raycast from mouse position to check if a target was clicked.
    /// If hit, remove it locally and report to server via RPC.
    private void TryHitTarget()
    {
        Vector2 mousePos = mouse.position.ReadValue();

        // Use EventSystem to check what UI element was clicked
        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = mousePos
        };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            // Check if the clicked object is a target (child of PlayArea)
            if (result.gameObject.transform.parent == TargetManager.Instance.playArea)
            {
                // HIT! Remove locally
                TargetManager.Instance.RemoveTarget(result.gameObject);

                // Report hit to server via RPC
                // Pattern: Client action → Server RPC → Server updates NetworkVariable
                NetworkGameManager.Instance.ReportHitRpc();
                return;
            }
        }
        // Missed — bullet wasted (no target hit)
    }

    // =========================================================================
    // RELOAD LOGIC
    // =========================================================================

    private void HandleReload()
    {
        // If jammed, right-click clears jam
        if (isJammed)
        {
            StartClearJam();
            return;
        }

        if (isReloading) return; // Already reloading
        if (currentBullets >= maxBullets) return; // Full
        StartReload();
    }

    private void StartReload()
    {
        if (isReloading) return;
        isReloading = true;
        reloadTextObject.SetActive(true);
        reloadText.text = "RELOADING...";
        reloadText.color = new Color(1f, 0.63f, 0f); // Orange
        reloadCoroutine = StartCoroutine(ReloadRoutine());
    }

    private void StopReload()
    {
        if (!isReloading) return;
        isReloading = false;
        if (reloadCoroutine != null) StopCoroutine(reloadCoroutine);
        reloadTextObject.SetActive(false);
    }

    /// Summary:
    /// Reload takes 1.5 seconds total. Bullets fill incrementally
    /// (0.25 seconds per bullet = 1.5s for 6 bullets).
    private IEnumerator ReloadRoutine()
    {
        float perBullet = reloadTime / maxBullets;
        while (currentBullets < maxBullets && isReloading)
        {
            yield return new WaitForSeconds(perBullet);
            if (!isReloading) yield break; // Interrupted
            currentBullets++;
            UpdateAmmoUI();
        }
        isReloading = false;
        reloadTextObject.SetActive(false);
    }

    // =========================================================================
    // GUN JAM
    // =========================================================================

    private void HandleJam()
    {
        if (!hasJamAbility) return;
        if (NetworkGameManager.Instance.phase.Value != NetworkGameManager.GamePhase.RoundActive) return;

        hasJamAbility = false;
        UpdateAbilityUI();

        // Send jam request to server
        // Pattern: Client → [Rpc(SendTo.Server)] → Server validates → Server broadcasts effect
        NetworkGameManager.Instance.RequestJamRpc();
    }

    /// Summary:
    /// Called by NetworkGameManager RPC when this client gets jammed.
    public void GetJammed()
    {
        if (isJammed) return;
        isJammed = true;
        StopReload(); // Cancel any reload

        reloadTextObject.SetActive(true);
        reloadText.text = "GUN JAMMED! CLICK TO CLEAR";
        reloadText.color = new Color(1f, 0.2f, 0.2f); // Red
    }

    private void StartClearJam()
    {
        if (!isJammed) return;
        if (jamCoroutine != null) return; // Already clearing
        jamCoroutine = StartCoroutine(ClearJamRoutine());
    }

    private IEnumerator ClearJamRoutine()
    {
        reloadText.text = "CLEARING JAM...";
        yield return new WaitForSeconds(jamClearTime);
        isJammed = false;
        reloadTextObject.SetActive(false);
        jamCoroutine = null;
    }

    // =========================================================================
    // POCKET SAND
    // =========================================================================

    private void HandleSand()
    {
        if (!hasSandAbility) return;
        if (NetworkGameManager.Instance.phase.Value != NetworkGameManager.GamePhase.RoundActive) return;

        hasSandAbility = false;
        UpdateAbilityUI();

        // Send sand request to server
        NetworkGameManager.Instance.RequestSandRpc();
    }

    // =========================================================================
    // RESET FOR NEW ROUND
    // =========================================================================

    /// Summary:
    /// Called at start of each round to reset ammo and states.
    public void ResetForNewRound()
    {
        currentBullets = maxBullets;
        isReloading = false;
        isJammed = false;
        if (reloadCoroutine != null) StopCoroutine(reloadCoroutine);
        if (jamCoroutine != null) StopCoroutine(jamCoroutine);
        reloadCoroutine = null;
        jamCoroutine = null;
        reloadTextObject.SetActive(false);

        // NOTE: hasJamAbility and hasSandAbility are NOT reset per round.
        // They are once per MATCH. They get reset in ResetForNewMatch().

        UpdateAmmoUI();
    }

    /// Summary:
    /// Called when a new match starts to restore abilities.
    public void ResetForNewMatch()
    {
        hasJamAbility = true;
        hasSandAbility = true;
        UpdateAbilityUI();
        ResetForNewRound();
    }

    // =========================================================================
    // UI UPDATES
    // =========================================================================

    private void UpdateAmmoUI()
    {
        for (int i = 0; i < bulletImages.Length; i++)
        {
            if (bulletImages[i] != null)
            {
                bulletImages[i].color = i < currentBullets
                    ? Color.white
                    : new Color(0.2f, 0.2f, 0.2f, 0.5f); // Dimmed = spent
            }
        }
    }

    private void UpdateAbilityUI()
    {
        if (jamIndicator != null)
            jamIndicator.color = hasJamAbility
                ? new Color(0.86f, 0.16f, 0.16f, 1f) // Bright red = available
                : new Color(0.3f, 0.1f, 0.1f, 0.4f);  // Dim = used

        if (sandIndicator != null)
            sandIndicator.color = hasSandAbility
                ? new Color(0.82f, 0.71f, 0.47f, 1f) // Bright tan = available
                : new Color(0.4f, 0.35f, 0.25f, 0.4f); // Dim = used
    }
}