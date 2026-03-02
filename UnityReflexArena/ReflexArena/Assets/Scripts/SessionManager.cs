// =============================================================================
// SessionManager.cs
// Attached to: GameManager (in Game scene)
// Purpose: EXTRA CREDIT — Named session displayed via NetworkVariable.
//          All clients see the session name set by the host.
//
// NETWORKING:
//   - NetworkVariable<FixedString64Bytes>: Syncs the session name across network.
//     FixedString64Bytes is used because NetworkVariable requires value types;
//     C# strings are reference types and cannot be used directly.
//   - OnValueChanged callback: Clients update session display when it changes.
//   - IsServer check: Only the host/server sets the session name.
// =============================================================================

using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using TMPro;

public class SessionManager : NetworkBehaviour
{
    public static SessionManager Instance { get; private set; }

    [Header("Session UI")]
    public TMP_Text sessionDisplayText;
    public TMP_Text hostIPText;

    /// Summary:
    /// Session name visible to all clients. Uses FixedString64Bytes
    /// because NetworkVariable<string> is not supported (string is a reference type).
    /// Server sets it; clients read via OnValueChanged.
    public NetworkVariable<FixedString64Bytes> sessionName =
        new NetworkVariable<FixedString64Bytes>(
            "Unnamed", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Subscribe to OnValueChanged — fires when server updates the name
        sessionName.OnValueChanged += OnSessionNameChanged;

        if (IsServer)
        {
            string name = MainMenuManager.ChosenSessionName;
            if (string.IsNullOrEmpty(name))
                name = "Reflex Arena - " + System.DateTime.Now.ToString("HH:mm");
            sessionName.Value = name;

            if (hostIPText != null)
            {
                hostIPText.text = $"Host IP: {GetLocalIP()}";
                hostIPText.gameObject.SetActive(true);
            }
        }

        UpdateDisplay(sessionName.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        sessionName.OnValueChanged -= OnSessionNameChanged;
        base.OnNetworkDespawn();
    }

    /// Summary:
    /// OnValueChanged callback — updates display on all clients.
    private void OnSessionNameChanged(FixedString64Bytes prev, FixedString64Bytes next)
    {
        UpdateDisplay(next.ToString());
    }

    private void UpdateDisplay(string name)
    {
        if (sessionDisplayText != null)
            sessionDisplayText.text = $"Session: {name}";
    }

    private string GetLocalIP()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }
}