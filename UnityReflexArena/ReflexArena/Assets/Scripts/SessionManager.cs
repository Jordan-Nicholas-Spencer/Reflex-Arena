using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Attached to: GameManager (or a dedicated UI manager object)
// Purpose: Named session display (extra credit helper).
//
// Summary:
// - Server sets a session name once on spawn.
// - Session name is shared to all clients via NetworkVariable<FixedString64Bytes>.
// - Also optionally shows host IPv4 address on the host for convenience.
//
// Note:
// This does NOT implement true discovery or Relay/Lobby. It provides a named
// session visible to all players and can be extended later.

public class SessionManager : NetworkBehaviour
{
    public static SessionManager Instance { get; private set; }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================

    [Header("Session UI")]
    [Tooltip("Host enters a session name before starting Host")]
    public TMP_InputField sessionNameInput;

    [Tooltip("Shows session name during gameplay")]
    public TMP_Text sessionDisplayText;

    [Tooltip("Shows host IPv4 address on host for convenience")]
    public TMP_Text hostIpText;

    // =========================================================================
    // NETWORK STATE
    // =========================================================================

    private static readonly FixedString64Bytes DefaultSessionName = "Unnamed Session";

    public NetworkVariable<FixedString64Bytes> SessionName = new(
        DefaultSessionName,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

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
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SessionName.OnValueChanged += OnSessionNameChanged;

        if (IsServer)
        {
            // Set session name once when the server spawns.
            // This is server-authoritative and replicates to all clients.
            string name = sessionNameInput != null ? sessionNameInput.text.Trim() : "";

            if (string.IsNullOrEmpty(name))
            {
                name = $"Reflex Arena {System.DateTime.Now:HH:mm}";
            }

            SessionName.Value = new FixedString64Bytes(name);

            // Host-only convenience display
            if (hostIpText != null)
            {
                hostIpText.text = $"Host IP: {GetLocalIPv4()}";
                hostIpText.gameObject.SetActive(true);
            }
        }
        else
        {
            // Non-host clients should not see host IP field
            if (hostIpText != null)
            {
                hostIpText.gameObject.SetActive(false);
            }
        }

        UpdateSessionDisplay(SessionName.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        SessionName.OnValueChanged -= OnSessionNameChanged;
        base.OnNetworkDespawn();
    }

    // =========================================================================
    // NETWORKVARIABLE CALLBACK
    // =========================================================================

    private void OnSessionNameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        UpdateSessionDisplay(newValue.ToString());
    }

    private void UpdateSessionDisplay(string sessionName)
    {
        if (sessionDisplayText == null) return;
        sessionDisplayText.text = $"Session: {sessionName}";
    }

    // =========================================================================
    // UTILITY
    // =========================================================================

    private static string GetLocalIPv4()
    {
        const string fallback = "127.0.0.1";

        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SessionManager] Could not get local IP: {e.Message}");
        }

        return fallback;
    }
}