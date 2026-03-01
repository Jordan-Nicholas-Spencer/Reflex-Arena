using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

// Attached to: GameManager (empty GameObject in scene)
// Purpose: Server-authoritative match flow for "Reflex Arena".
//
// Summary:
// - Server controls round state, target spawning, scoring, and match end.
// - Clients send click actions via RPC.
// - Shared state is synced via NetworkVariables (clients react via OnValueChanged).

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================

    [Header("UI Panels")]
    [Tooltip("Shown before networking starts (Host/Join/Server)")]
    public GameObject connectionPanel;

    [Tooltip("Main in-game UI (play area, scoreboard, etc.)")]
    public GameObject gamePanel;

    [Tooltip("Shown when match is over")]
    public GameObject winnerPanel;

    [Header("UI Text")]
    [Tooltip("Displays current round")]
    public TMP_Text roundText;

    [Tooltip("Displays current status (Waiting, Get ready, Click target, etc.)")]
    public TMP_Text statusText;

    [Tooltip("Displays match winner")]
    public TMP_Text winnerText;

    [Header("UI Buttons")]
    [Tooltip("Restarts the match after MatchOver")]
    public Button restartButton;

    [Header("Connection UI")]
    [Tooltip("IP input for client connections (default: 127.0.0.1)")]
    public TMP_InputField ipInputField;

    public Button hostButton;
    public Button clientButton;
    public Button serverButton;

    [Header("Game Settings")]
    [Min(1)] public int winsNeeded = 3;
    [Min(1)] public int maxRounds = 5;
    [Range(1, 4)] public int maxPlayersSupported = 4;
    [Min(1)] public int minPlayersToStart = 2;

    [Header("Round Timing")]
    [Tooltip("Seconds before the round starts (Get ready)")]
    public float roundStartDelay = 2.0f;

    [Tooltip("Random delay before target appears")]
    public Vector2 randomSpawnDelayRange = new Vector2(0.8f, 2.5f);

    [Tooltip("Seconds before forcing round end if not all players clicked")]
    public float roundTimeoutSeconds = 5.0f;

    [Tooltip("Seconds to display round result before next round")]
    public float roundEndDelay = 3.0f;

    // =========================================================================
    // NETWORK STATE (SERVER WRITES, EVERYONE READS)
    // =========================================================================

    public enum GameState
    {
        WaitingForPlayers,
        RoundStarting,
        RoundActive,
        RoundEnded,
        MatchOver
    }

    [Header("NetworkVariables — Server Authoritative")]
    public NetworkVariable<int> CurrentRound = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<GameState> State = new(
        GameState.WaitingForPlayers,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> PlayerCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<ScoreData> Scores = new(
        new ScoreData(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // NetworkVariable requires value type + change detection.
    public struct ScoreData : INetworkSerializable, System.IEquatable<ScoreData>
    {
        public int p0;
        public int p1;
        public int p2;
        public int p3;

        public int Get(int index)
        {
            return index switch
            {
                0 => p0,
                1 => p1,
                2 => p2,
                3 => p3,
                _ => 0
            };
        }

        public void Set(int index, int value)
        {
            switch (index)
            {
                case 0: p0 = value; break;
                case 1: p1 = value; break;
                case 2: p2 = value; break;
                case 3: p3 = value; break;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref p0);
            serializer.SerializeValue(ref p1);
            serializer.SerializeValue(ref p2);
            serializer.SerializeValue(ref p3);
        }

        public bool Equals(ScoreData other)
        {
            return p0 == other.p0 &&
                   p1 == other.p1 &&
                   p2 == other.p2 &&
                   p3 == other.p3;
        }
    }

    // =========================================================================
    // LOCAL (NON-NETWORKED) STATE
    // =========================================================================

    // Server only: click data for the current round
    private readonly Dictionary<ulong, float> _clickTimes = new();
    private readonly Dictionary<ulong, float> _clickDistances = new();

    // Client only: prevent double-clicks per round
    private bool _localClickedThisRound;

    // Server only: target spawn time for reaction time calculation
    private float _targetSpawnTime;

    // Shared mapping (maintained by server, mirrored to clients via RPC)
    private readonly Dictionary<ulong, int> _playerIndexByClientId = new();
    private int _nextPlayerIndex;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Wire buttons (safe even if panels are disabled initially)
        hostButton.onClick.AddListener(StartHost);
        clientButton.onClick.AddListener(StartClient);
        serverButton.onClick.AddListener(StartServer);
        restartButton.onClick.AddListener(OnRestartClicked);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to NetworkVariable changes
        CurrentRound.OnValueChanged += (_, v) => UpdateRoundUI(v);
        Scores.OnValueChanged += (_, v) => UpdateScoreboardUI(v);
        State.OnValueChanged += (_, v) => UpdateGameStateUI(v);
        PlayerCount.OnValueChanged += (_, v) => UpdatePlayerCountUI(v);

        // UI defaults once connected/spawned
        connectionPanel.SetActive(false);
        gamePanel.SetActive(true);
        winnerPanel.SetActive(false);

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

            AssignPlayerIndex(NetworkManager.Singleton.LocalClientId);
            NotifyPlayerIndexRpc(NetworkManager.Singleton.LocalClientId, _playerIndexByClientId[NetworkManager.Singleton.LocalClientId]);
        }

        // Force initial UI state
        UpdateRoundUI(CurrentRound.Value);
        UpdateScoreboardUI(Scores.Value);
        UpdateGameStateUI(State.Value);
        UpdatePlayerCountUI(PlayerCount.Value);
    }

    public override void OnNetworkDespawn()
    {
        CurrentRound.OnValueChanged -= (_, _) => { };
        Scores.OnValueChanged -= (_, _) => { };
        State.OnValueChanged -= (_, _) => { };
        PlayerCount.OnValueChanged -= (_, _) => { };

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        base.OnNetworkDespawn();
    }

    // =========================================================================
    // CONNECTION BUTTONS
    // =========================================================================

    private void StartHost()
    {
        NetworkManager.Singleton.StartHost();
    }

    private void StartServer()
    {
        NetworkManager.Singleton.StartServer();
    }

    private void StartClient()
    {
        string ip = ipInputField != null ? ipInputField.text.Trim() : "";
        if (string.IsNullOrEmpty(ip))
        {
            ip = "127.0.0.1";
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.ConnectionData.Address = ip;

        NetworkManager.Singleton.StartClient();
    }

    // =========================================================================
    // SERVER: CONNECT/DISCONNECT
    // =========================================================================

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        AssignPlayerIndex(clientId);
        PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

        Debug.Log($"[Server] Client {clientId} connected. Players: {PlayerCount.Value}");

        // Share mapping to all clients
        NotifyPlayerIndexRpc(clientId, _playerIndexByClientId[clientId]);

        // Send existing mappings to the newly connected client
        foreach (var kvp in _playerIndexByClientId)
        {
            if (kvp.Key == clientId) continue;
            NotifyPlayerIndexTargetRpc(kvp.Key, kvp.Value, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        // Start match if ready
        if (PlayerCount.Value >= minPlayersToStart && State.Value == GameState.WaitingForPlayers)
        {
            StartCoroutine(StartRoundAfterDelay(roundStartDelay));
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        PlayerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
        Debug.Log($"[Server] Client {clientId} disconnected. Players: {PlayerCount.Value}");
    }

    private void AssignPlayerIndex(ulong clientId)
    {
        if (_playerIndexByClientId.ContainsKey(clientId)) return;
        if (_nextPlayerIndex >= maxPlayersSupported) return;

        _playerIndexByClientId[clientId] = _nextPlayerIndex;
        _nextPlayerIndex++;
    }

    public int GetPlayerIndex(ulong clientId)
    {
        return _playerIndexByClientId.TryGetValue(clientId, out int index) ? index : -1;
    }

    // =========================================================================
    // RPC: PLAYER INDEX MAPPING
    // =========================================================================

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPlayerIndexRpc(ulong clientId, int index)
    {
        if (!_playerIndexByClientId.ContainsKey(clientId))
        {
            _playerIndexByClientId[clientId] = index;
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void NotifyPlayerIndexTargetRpc(ulong clientId, int index, RpcParams rpcParams = default)
    {
        if (!_playerIndexByClientId.ContainsKey(clientId))
        {
            _playerIndexByClientId[clientId] = index;
        }
    }

    // =========================================================================
    // UI UPDATES (CLIENT-SIDE)
    // =========================================================================

    private void UpdateRoundUI(int round)
    {
        if (round <= 0)
        {
            roundText.text = "Waiting to start...";
            return;
        }

        roundText.text = $"Round {round} of {maxRounds}";
    }

    private void UpdateScoreboardUI(ScoreData scoreData)
    {
        if (ScoreboardUI.Instance == null) return;
        ScoreboardUI.Instance.UpdateScoreboard(scoreData, _playerIndexByClientId, PlayerCount.Value);
    }

    private void UpdatePlayerCountUI(int count)
    {
        if (State.Value == GameState.WaitingForPlayers)
        {
            statusText.text = $"Waiting for players... ({count} connected, need {minPlayersToStart})";
        }
    }

    private void UpdateGameStateUI(GameState state)
    {
        switch (state)
        {
            case GameState.WaitingForPlayers:
                statusText.text = $"Waiting for players... ({PlayerCount.Value} connected)";
                winnerPanel.SetActive(false);
                _localClickedThisRound = false;
                break;

            case GameState.RoundStarting:
                statusText.text = "Get ready...";
                _localClickedThisRound = false;
                break;

            case GameState.RoundActive:
                statusText.text = "CLICK THE TARGET!";
                _localClickedThisRound = false;
                break;

            case GameState.RoundEnded:
                // Status text is set via RPC with round result
                _localClickedThisRound = false;
                break;

            case GameState.MatchOver:
                // Winner UI is set via RPC
                _localClickedThisRound = false;
                break;
        }
    }

    // =========================================================================
    // SERVER: MATCH FLOW
    // =========================================================================

    private IEnumerator StartRoundAfterDelay(float delay)
    {
        if (!IsServer) yield break;

        State.Value = GameState.RoundStarting;
        CurrentRound.Value++;

        yield return new WaitForSeconds(delay);

        float randomDelay = Random.Range(randomSpawnDelayRange.x, randomSpawnDelayRange.y);
        yield return new WaitForSeconds(randomDelay);

        _clickTimes.Clear();
        _clickDistances.Clear();

        _targetSpawnTime = Time.time;

        TargetManager.Instance.SpawnTarget();
        State.Value = GameState.RoundActive;

        ResetLocalClickTrackingRpc();

        StartCoroutine(RoundTimeout(roundTimeoutSeconds));
    }

    private IEnumerator RoundTimeout(float timeout)
    {
        yield return new WaitForSeconds(timeout);

        if (IsServer && State.Value == GameState.RoundActive)
        {
            EndRound();
        }
    }

    // Called by server when a client click RPC arrives
    private void RegisterClick(ulong clientId, float distancePx)
    {
        if (!IsServer) return;
        if (State.Value != GameState.RoundActive) return;
        if (_clickTimes.ContainsKey(clientId)) return;

        float reactionTime = Time.time - _targetSpawnTime;
        _clickTimes[clientId] = reactionTime;
        _clickDistances[clientId] = distancePx;

        Debug.Log($"[Server] Client {clientId} clicked. Time={reactionTime:F3}s Dist={distancePx:F1}px");

        int expectedClicks = NetworkManager.Singleton.ConnectedClientsIds.Count;
        if (_clickTimes.Count >= expectedClicks)
        {
            EndRound();
        }
    }

    private void EndRound()
    {
        if (!IsServer) return;

        State.Value = GameState.RoundEnded;
        TargetManager.Instance.HideTarget();

        if (_clickTimes.Count == 0)
        {
            AnnounceRoundResultRpc("No one clicked. No winner this round.", -1);
            StartCoroutine(StartRoundAfterDelay(roundEndDelay));
            return;
        }

        ulong winnerClientId = 0;
        float bestTime = float.MaxValue;

        foreach (var kvp in _clickTimes)
        {
            ulong cid = kvp.Key;
            float time = kvp.Value;

            if (time < bestTime)
            {
                bestTime = time;
                winnerClientId = cid;
                continue;
            }

            if (Mathf.Approximately(time, bestTime))
            {
                if (_clickDistances[cid] < _clickDistances[winnerClientId])
                {
                    winnerClientId = cid;
                }
            }
        }

        int winnerIndex = GetPlayerIndex(winnerClientId);
        if (winnerIndex < 0)
        {
            AnnounceRoundResultRpc("Winner could not be mapped to a player index.", -1);
            StartCoroutine(StartRoundAfterDelay(roundEndDelay));
            return;
        }

        // Update scores (NetworkVariable write)
        ScoreData updated = Scores.Value;
        updated.Set(winnerIndex, updated.Get(winnerIndex) + 1);
        Scores.Value = updated;

        int winnerScore = updated.Get(winnerIndex);

        AnnounceRoundResultRpc($"Player {winnerIndex + 1} wins the round! ({bestTime:F3}s)", winnerIndex);

        // Match win by score
        if (winnerScore >= winsNeeded)
        {
            State.Value = GameState.MatchOver;
            AnnounceMatchWinnerRpc(winnerIndex);
            return;
        }

        // Match over by max rounds
        if (CurrentRound.Value >= maxRounds)
        {
            int bestPlayer = -1;
            int bestScore = -1;

            for (int i = 0; i < maxPlayersSupported; i++)
            {
                int score = updated.Get(i);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = i;
                }
            }

            State.Value = GameState.MatchOver;
            AnnounceMatchWinnerRpc(bestPlayer);
            return;
        }

        StartCoroutine(StartRoundAfterDelay(roundEndDelay));
    }

    // =========================================================================
    // RPC: ROUND/MATCH UI MESSAGES
    // =========================================================================

    [Rpc(SendTo.ClientsAndHost)]
    private void ResetLocalClickTrackingRpc()
    {
        _localClickedThisRound = false;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceRoundResultRpc(string message, int winnerIndex)
    {
        statusText.text = message;
        Debug.Log($"[Client] Round result: {message}");
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceMatchWinnerRpc(int winnerIndex)
    {
        winnerText.text = winnerIndex >= 0
            ? $"Player {winnerIndex + 1} Wins the Match!"
            : "Match Over!";

        statusText.text = "Match Over!";
        winnerPanel.SetActive(true);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void HideWinnerPanelRpc()
    {
        winnerPanel.SetActive(false);
    }

    // =========================================================================
    // CLIENT -> SERVER ACTION RPCs
    // =========================================================================

    [Rpc(SendTo.Server)]
    public void PlayerClickRpc(float distancePx, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        RegisterClick(senderId, distancePx);
    }

    // Client-side gate to prevent double click spam
    public bool TryLocalClickGate()
    {
        if (_localClickedThisRound) return false;
        if (State.Value != GameState.RoundActive) return false;

        _localClickedThisRound = true;
        return true;
    }

    private void OnRestartClicked()
    {
        if (IsServer)
        {
            RestartMatch();
            return;
        }

        RequestRestartRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestRestartRpc()
    {
        RestartMatch();
    }

    private void RestartMatch()
    {
        if (!IsServer) return;

        CurrentRound.Value = 0;
        Scores.Value = new ScoreData();
        State.Value = GameState.WaitingForPlayers;

        TargetManager.Instance.HideTarget();
        HideWinnerPanelRpc();

        if (PlayerCount.Value >= minPlayersToStart)
        {
            StartCoroutine(StartRoundAfterDelay(roundStartDelay));
        }
    }
}