// =============================================================================
// NetworkGameManager.cs
// Attached to: GameManager (in Game scene)
// Required Components on same GameObject: NetworkObject
// Purpose: Server-authoritative round control, scoring, match flow.
//
// NETWORKING:
//   - NetworkVariable<T>: Server-authoritative state synced to all clients.
//     Only server writes; clients read via OnValueChanged callbacks.
//   - [Rpc(SendTo.Server)]: Client → Server communication.
//   - [Rpc(SendTo.ClientsAndHost)]: Server → All Clients communication.
//   - OnNetworkSpawn(): Called when object is network-ready.
//   - IsServer / IsClient: Authority checks before operations.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

public class NetworkGameManager : NetworkBehaviour
{
    // =========================================================================
    // SINGLETON
    // =========================================================================
    public static NetworkGameManager Instance { get; private set; }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    [Header("UI Text")]
    public TMP_Text roundText;
    public TMP_Text countdownText;
    public TMP_Text timerText;
    public TMP_Text winnerText;

    [Header("UI Panels/Overlays")]
    public GameObject countdownObject;   // The CountdownText GameObject
    public GameObject timerObject;       // The TimerText GameObject
    public GameObject scoreboardOverlay;
    public GameObject winnerPanel;

    [Header("UI Buttons")]
    public Button playAgainButton;
    public Button quitToMenuButton;

    [Header("Game Settings")]
    public int winsNeeded = 3;
    public int maxRounds = 5;

    [Header("Target Settings")]
    [Tooltip("Exact number of targets in round 1")]
    public int round1TargetCount = 10;

    [Tooltip("Minimum targets in rounds after round 1")]
    public int laterRoundsTargetMin = 10;

    [Tooltip("Maximum targets in rounds after round 1")]
    public int laterRoundsTargetMax = 15;

    [Tooltip("Base time in seconds for each round (before target count bonus)")]
    public float baseRoundTime = 5f;

    [Tooltip("Extra seconds added per target")]
    public float timePerTarget = 0.3f;

    // =========================================================================
    // NETWORK VARIABLES — Server-authoritative
    // Only the server writes to these. All clients receive updates via
    // OnValueChanged callbacks subscribed in OnNetworkSpawn().
    //
    // NetworkVariable<T> requires T to be a value type (struct) that
    // implements INetworkSerializable and IEquatable<T>.
    // =========================================================================

    /// Summary:
    /// Current round number (1-based). Server increments each round.    
    public NetworkVariable<int> currentRound = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// Summary:
    /// Per-player round wins (up to 4 players).
    public NetworkVariable<ScoreData> roundWins = new NetworkVariable<ScoreData>(
        new ScoreData(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// Summary:
    /// Per-player hit count for the CURRENT round.
    public NetworkVariable<ScoreData> roundHits = new NetworkVariable<ScoreData>(
        new ScoreData(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// Summary:
    /// Current phase of the game.
    public NetworkVariable<GamePhase> phase = new NetworkVariable<GamePhase>(
        GamePhase.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// Summary:
    /// Number of connected players.
    public NetworkVariable<int> playerCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// Summary:
    /// Number of targets for the current round.
    public NetworkVariable<int> targetCountThisRound = new NetworkVariable<int>(
        10, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// Summary:
    /// Time remaining in the current round (seconds).
    public NetworkVariable<float> roundTimeRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // =========================================================================
    // ENUMS & STRUCTS
    // =========================================================================

    public enum GamePhase
    {
        WaitingForPlayers,
        Countdown,       // 3-second pre-round countdown
        RoundActive,     // Targets visible, players shooting
        RoundResult,     // Showing scoreboard between rounds
        MatchOver
    }

    /// Summary:
    /// Score data for up to 4 players. Must implement INetworkSerializable
    /// so Netcode knows how to serialize it, and IEquatable so NetworkVariable
    /// can detect when the value has actually changed.
    public struct ScoreData : INetworkSerializable, System.IEquatable<ScoreData>
    {
        public int p0, p1, p2, p3;

        public int Get(int i) {
            switch(i) { case 0: return p0; case 1: return p1; case 2: return p2; case 3: return p3; default: return 0; }
        }
        public void Set(int i, int v) {
            switch(i) { case 0: p0=v; break; case 1: p1=v; break; case 2: p2=v; break; case 3: p3=v; break; }
        }
        public void Add(int i, int v) { Set(i, Get(i) + v); }

        // INetworkSerializable: tells Netcode how to send this across the wire
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref p0);
            s.SerializeValue(ref p1);
            s.SerializeValue(ref p2);
            s.SerializeValue(ref p3);
        }

        // IEquatable: NetworkVariable uses this to detect changes
        public bool Equals(ScoreData o) => p0==o.p0 && p1==o.p1 && p2==o.p2 && p3==o.p3;
    }

    // =========================================================================
    // LOCAL STATE (not synced — each instance tracks its own)
    // =========================================================================
    private Dictionary<ulong, int> playerIndexMap = new Dictionary<ulong, int>();
    private int nextPlayerIndex = 0;
    private float roundDuration = 8f; // seconds per round for targets to remain
    private Coroutine roundTimerCoroutine;
    private bool isSinglePlayer = false;

    // =========================================================================
    // AWAKE
    // =========================================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // =========================================================================
    // START — Auto-connect based on MainMenu selection
    // =========================================================================
    private void Start()
    {
        playAgainButton.onClick.AddListener(OnPlayAgain);
        quitToMenuButton.onClick.AddListener(OnQuitToMenu);

        isSinglePlayer = MainMenuManager.IsSinglePlayer;

        // Auto-start networking based on menu choice
        switch (MainMenuManager.ChosenMode)
        {
            case MainMenuManager.ConnectionMode.Host:
            case MainMenuManager.ConnectionMode.SinglePlayer:
                NetworkManager.Singleton.StartHost();
                break;

            case MainMenuManager.ConnectionMode.Client:
                var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                transport.ConnectionData.Address = MainMenuManager.ChosenIP;
                NetworkManager.Singleton.StartClient();
                break;
        }
    }

    // =========================================================================
    // OnNetworkSpawn — Subscribe to OnValueChanged callbacks
    //
    // This is THE critical lifecycle method for networked objects. It's called
    // once the object is fully registered on the network. This is where 
    // NetworkVariable callbacks are wired up.
    // =========================================================================
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // --- SUBSCRIBE TO OnValueChanged CALLBACKS ---
        // These fire on ALL clients when the server updates the variable.
        // Callback signature: (T previousValue, T newValue)
        currentRound.OnValueChanged += OnRoundChanged;
        roundWins.OnValueChanged += OnWinsChanged;
        roundHits.OnValueChanged += OnHitsChanged;
        phase.OnValueChanged += OnPhaseChanged;
        playerCount.OnValueChanged += OnPlayerCountChanged;
        roundTimeRemaining.OnValueChanged += OnTimerChanged;

        // Server setup
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            playerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
            AssignPlayerIndex(NetworkManager.Singleton.LocalClientId);

            // Single player: spawn bot and start immediately
            if (isSinglePlayer)
            {
                // Bot gets player index 1
                if (!playerIndexMap.ContainsKey(999))
                {
                    playerIndexMap[999] = 1;
                    nextPlayerIndex = 2;
                }
                playerCount.Value = 2;
                StartCoroutine(BeginCountdown());
            }
        }

        // Initial UI state
        winnerPanel.SetActive(false);
        scoreboardOverlay.SetActive(false);
        countdownObject.SetActive(false);
        timerObject.SetActive(false);
        UpdateRoundText();
    }

    public override void OnNetworkDespawn()
    {
        currentRound.OnValueChanged -= OnRoundChanged;
        roundWins.OnValueChanged -= OnWinsChanged;
        roundHits.OnValueChanged -= OnHitsChanged;
        phase.OnValueChanged -= OnPhaseChanged;
        playerCount.OnValueChanged -= OnPlayerCountChanged;
        roundTimeRemaining.OnValueChanged -= OnTimerChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnNetworkDespawn();
    }

    // =========================================================================
    // SERVER: Client Connection
    // =========================================================================
    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        AssignPlayerIndex(clientId);
        playerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

        // Send all existing mappings to the new client
        // NOTE: We copy to a list first because BroadcastPlayerIndexRpc executes
        // immediately on the host (which modifies playerIndexMap), and you cannot
        // modify a Dictionary while iterating over it.
        var existingMappings = new List<KeyValuePair<ulong, int>>(playerIndexMap);
        foreach (var kvp in existingMappings)
            BroadcastPlayerIndexRpc(kvp.Key, kvp.Value);

        // Start game when 2+ players connect
        if (!isSinglePlayer && playerCount.Value >= 2 && phase.Value == GamePhase.WaitingForPlayers)
            StartCoroutine(BeginCountdown());
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        playerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
    }

    private void AssignPlayerIndex(ulong clientId)
    {
        if (!playerIndexMap.ContainsKey(clientId) && nextPlayerIndex < 4)
        {
            playerIndexMap[clientId] = nextPlayerIndex;
            nextPlayerIndex++;
        }
    }

    public int GetPlayerIndex(ulong clientId)
    {
        return playerIndexMap.TryGetValue(clientId, out int idx) ? idx : -1;
    }

    // =========================================================================
    // RPCs — Player Index Sync
    //
    // [Rpc(SendTo.ClientsAndHost)] = Server calls this, executes on all clients.
    // This ensures every client knows the mapping of clientId → player index.
    // =========================================================================

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastPlayerIndexRpc(ulong clientId, int index)
    {
        playerIndexMap[clientId] = index;
    }

    // =========================================================================
    // OnValueChanged CALLBACKS
    //
    // These implement the pattern: Server updates NetworkVariable →
    // OnValueChanged fires on all clients → clients update UI.
    // =========================================================================

    private void OnRoundChanged(int prev, int next) => UpdateRoundText();
    private void OnWinsChanged(ScoreData prev, ScoreData next) => UpdateScoreboardValues();
    private void OnHitsChanged(ScoreData prev, ScoreData next) { /* HUD updates in real-time via separate method */ }
    private void OnPlayerCountChanged(int prev, int next) => UpdateRoundText();

    private void OnTimerChanged(float prev, float next)
    {
        if (timerText != null)
            timerText.text = next.ToString("F1");
    }

    private void OnPhaseChanged(GamePhase prev, GamePhase next)
    {
        switch (next)
        {
            case GamePhase.WaitingForPlayers:
                roundText.text = "Waiting for players...";
                scoreboardOverlay.SetActive(false);
                countdownObject.SetActive(false);
                timerObject.SetActive(false);
                winnerPanel.SetActive(false);
                break;

            case GamePhase.Countdown:
                scoreboardOverlay.SetActive(false);
                winnerPanel.SetActive(false);
                countdownObject.SetActive(true);
                timerObject.SetActive(false);
                break;

            case GamePhase.RoundActive:
                countdownObject.SetActive(false);
                timerObject.SetActive(true);
                scoreboardOverlay.SetActive(false);
                break;

            case GamePhase.RoundResult:
                timerObject.SetActive(false);
                scoreboardOverlay.SetActive(true);
                UpdateScoreboardValues();
                break;

            case GamePhase.MatchOver:
                timerObject.SetActive(false);
                scoreboardOverlay.SetActive(false);
                winnerPanel.SetActive(true);
                break;
        }
    }

    // =========================================================================
    // UI HELPERS
    // =========================================================================

    /// Summary:
    /// Updates round text. Shows "Round X", "Match Point: Player X",
    /// or "Next Point Wins" as appropriate.
    private void UpdateRoundText()
    {
        if (currentRound.Value <= 0)
        {
            roundText.text = phase.Value == GamePhase.WaitingForPlayers
                ? $"Waiting for players... ({playerCount.Value} connected)"
                : "";
            return;
        }

        // Check for match point situations
        ScoreData wins = roundWins.Value;
        int playersAtMatchPoint = 0;
        int matchPointPlayer = -1;

        int activePlayers = Mathf.Min(playerCount.Value, 4);
        for (int i = 0; i < activePlayers; i++)
        {
            if (wins.Get(i) == winsNeeded - 1)
            {
                playersAtMatchPoint++;
                matchPointPlayer = i;
            }
        }

        if (playersAtMatchPoint > 1)
            roundText.text = "NEXT POINT WINS";
        else if (playersAtMatchPoint == 1)
            roundText.text = $"MATCH POINT: Player {matchPointPlayer + 1}";
        else
            roundText.text = $"Round {currentRound.Value}";
    }

    private void UpdateScoreboardValues()
    {
        if (ScoreboardUI.Instance != null)
            ScoreboardUI.Instance.Refresh(roundWins.Value, roundHits.Value, playerCount.Value, currentRound.Value);
    }

    // =========================================================================
    // SERVER: ROUND FLOW
    // =========================================================================

    /// Summary:
    /// 3-second countdown before each round.
    private IEnumerator BeginCountdown()
    {
        if (!IsServer) yield break;

        currentRound.Value++;
        phase.Value = GamePhase.Countdown;

        // Determine target count: round 1 = 10, later = 10-15
        int count = currentRound.Value == 1 ? round1TargetCount : Random.Range(laterRoundsTargetMin, laterRoundsTargetMax + 1);
        targetCountThisRound.Value = count;

        // Reset round hits
        roundHits.Value = new ScoreData();

        // 3-second countdown displayed on all clients
        for (int i = 3; i >= 1; i--)
        {
            UpdateCountdownRpc(i.ToString());
            yield return new WaitForSeconds(1f);
        }

        UpdateCountdownRpc("GO!");
        yield return new WaitForSeconds(0.4f);

        // Generate random seed for target positions so all clients get same positions
        int seed = Random.Range(0, int.MaxValue);

        // Start the round
        phase.Value = GamePhase.RoundActive;

        // Tell all clients to spawn targets with the same seed
        SpawnTargetsRpc(count, seed);

        // Reset all player states (ammo, abilities) via RPC
        ResetPlayerStatesRpc();

        // Start round timer
        float duration = baseRoundTime + (count * timePerTarget); // more targets = more time
        roundDuration = duration;
        roundTimeRemaining.Value = duration;

        if (roundTimerCoroutine != null) StopCoroutine(roundTimerCoroutine);
        roundTimerCoroutine = StartCoroutine(RoundTimerRoutine(duration));

        // If single player, start bot
        if (isSinglePlayer && BotPlayer.Instance != null)
            BotPlayer.Instance.StartBotRound(count);
    }

    /// Summary:
    /// Server counts down the round timer and ends when it hits 0.
    private IEnumerator RoundTimerRoutine(float duration)
    {
        float remaining = duration;
        while (remaining > 0f && phase.Value == GamePhase.RoundActive)
        {
            remaining -= Time.deltaTime;
            roundTimeRemaining.Value = Mathf.Max(0f, remaining);
            yield return null;
        }

        if (phase.Value == GamePhase.RoundActive)
            EndRound();
    }

    // =========================================================================
    // RPCs — Round Flow (Server → All Clients)
    // =========================================================================

    /// Summary:
    /// [Server → Clients] Update the countdown display text.
    [Rpc(SendTo.ClientsAndHost)]
    private void UpdateCountdownRpc(string text)
    {
        if (countdownText != null)
            countdownText.text = text;
    }

    /// Summary:
    /// [Server → Clients] Tell all clients to spawn targets locally.
    /// Uses a shared seed so Random.Range produces identical positions everywhere.
    /// This is how "elements spawned on the server" work — the server decides
    /// the seed/parameters, broadcasts them, and each client creates locally.
    /// 
    [Rpc(SendTo.ClientsAndHost)]
    private void SpawnTargetsRpc(int count, int seed)
    {
        if (TargetManager.Instance != null)
            TargetManager.Instance.SpawnTargets(count, seed);
    }

    /// Summary:
    /// [Server → Clients] Reset player ammo and abilities each round.
    [Rpc(SendTo.ClientsAndHost)]
    private void ResetPlayerStatesRpc()
    {
        if (PlayerInputHandler.Instance != null)
            PlayerInputHandler.Instance.ResetForNewRound();
    }

    // =========================================================================
    // SERVER: Receiving Player Hits
    //
    // [Rpc(SendTo.Server)]: Client → Server.
    // The client tells the server "I hit a target". The server validates
    // and updates the NetworkVariable. This is the pattern:
    // Client action → Server RPC → Server updates NetworkVariable → 
    // OnValueChanged fires on all clients.
    // =========================================================================

    /// Summary:
    /// [Client → Server] A player reports hitting a target.
    /// Server validates and increments their hit count.
    /// 
    /// rpcParams.Receive.SenderClientId identifies which client sent this.
    [Rpc(SendTo.Server)]
    public void ReportHitRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        int idx = GetPlayerIndex(sender);
        if (idx < 0) return;
        if (phase.Value != GamePhase.RoundActive) return;

        ScoreData hits = roundHits.Value;
        hits.Add(idx, 1);
        roundHits.Value = hits; // Triggers OnValueChanged on all clients
    }

    /// Summary:
    /// SERVER: Called by BotPlayer to register a bot hit.
    public void RegisterBotHit()
    {
        if (!IsServer) return;
        if (phase.Value != GamePhase.RoundActive) return;

        ScoreData hits = roundHits.Value;
        hits.Add(1, 1); // Bot is always player index 1
        roundHits.Value = hits;
    }

    // =========================================================================
    // SERVER: End Round — Determine winner, update scores
    // =========================================================================

    private void EndRound()
    {
        if (!IsServer) return;

        phase.Value = GamePhase.RoundResult;

        // Hide targets on all clients
        HideAllTargetsRpc();

        // Find round winner (most hits)
        ScoreData hits = roundHits.Value;
        int bestHits = -1;
        int bestPlayer = -1;
        int activePlayers = Mathf.Min(playerCount.Value, 4);

        for (int i = 0; i < activePlayers; i++)
        {
            if (hits.Get(i) > bestHits)
            {
                bestHits = hits.Get(i);
                bestPlayer = i;
            }
        }

        // Award round win
        if (bestPlayer >= 0 && bestHits > 0)
        {
            ScoreData wins = roundWins.Value;
            wins.Add(bestPlayer, 1);
            roundWins.Value = wins; // Triggers OnValueChanged → scoreboard updates

            // Check for match winner
            if (wins.Get(bestPlayer) >= winsNeeded)
            {
                phase.Value = GamePhase.MatchOver;
                AnnounceWinnerRpc(bestPlayer);
                return;
            }
        }

        // Show scoreboard for 4 seconds, then start next round
        StartCoroutine(InterRoundPause());
    }

    private IEnumerator InterRoundPause()
    {
        // Scoreboard is shown (phase = RoundResult triggers OnPhaseChanged)
        yield return new WaitForSeconds(4f);
        StartCoroutine(BeginCountdown());
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void HideAllTargetsRpc()
    {
        if (TargetManager.Instance != null)
            TargetManager.Instance.ClearAllTargets();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceWinnerRpc(int winnerIndex)
    {
        winnerText.text = $"Player {winnerIndex + 1} Wins the Match!";
        winnerPanel.SetActive(true);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ResetPlayerMatchStatesRpc()
    {
        if (PlayerInputHandler.Instance != null)
            PlayerInputHandler.Instance.ResetForNewMatch();
    }

    // =========================================================================
    // MULTIPLAYER INTERACTIONS: Jam & Sand
    //
    // These are [Rpc(SendTo.Server)] calls — clients request an action,
    // server validates (checks if ability available), then broadcasts
    // the effect to the targeted client(s) via [Rpc(SendTo.ClientsAndHost)].
    // =========================================================================

    /// Summary:
    /// [Client → Server] Player requests to jam opponents' guns.
    /// Server validates and applies.
    [Rpc(SendTo.Server)]
    public void RequestJamRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        // Apply jam to all OTHER players
        ApplyJamRpc(sender);
    }

    /// Summary:
    /// [Server → All Clients] Apply gun jam to everyone except the sender.
    /// Each client checks if they are the sender; if not, they get jammed.
    [Rpc(SendTo.ClientsAndHost)]
    private void ApplyJamRpc(ulong senderClientId)
    {
        // Don't jam the player who sent it
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        if (PlayerInputHandler.Instance != null)
            PlayerInputHandler.Instance.GetJammed();
    }

    /// Summary:
    /// [Client → Server] Player requests to throw pocket sand at opponents.
    [Rpc(SendTo.Server)]
    public void RequestSandRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        ApplySandRpc(sender);
    }

    /// Summary:
    /// [Server → All Clients] Apply pocket sand to everyone except sender.
    [Rpc(SendTo.ClientsAndHost)]
    private void ApplySandRpc(ulong senderClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        if (PocketSandEffect.Instance != null)
            PocketSandEffect.Instance.ActivateSand();
    }

    // =========================================================================
    // RESTART / QUIT
    // =========================================================================

    private void OnPlayAgain()
    {
        if (IsServer) RestartMatch();
        else RequestRestartRpc();
    }

    /// Summary:
    /// [Client → Server] Client requests a restart.
    [Rpc(SendTo.Server)]
    private void RequestRestartRpc() => RestartMatch();

    private void RestartMatch()
    {
        if (!IsServer) return;
        currentRound.Value = 0;
        roundWins.Value = new ScoreData();
        roundHits.Value = new ScoreData();
        phase.Value = GamePhase.WaitingForPlayers;
        HideAllTargetsRpc();
        HideWinnerRpc();
        ResetPlayerMatchStatesRpc();

        if (playerCount.Value >= 2 || isSinglePlayer)
            StartCoroutine(BeginCountdown());
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void HideWinnerRpc() => winnerPanel.SetActive(false);

    private void OnQuitToMenu()
    {
        // Shutdown networking and return to main menu
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene("MainMenu");
    }
}