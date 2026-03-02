// =============================================================================
// MainMenuManager.cs
// Attached to: MainMenuManager (in MainMenu scene)
// Purpose: Handles main menu UI: Host, Join, Single Player, How to Play, Quit.
//          Sets connection mode and loads the Game scene.
//
// NOT a NetworkBehaviour — runs before networking starts.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// Summary:
/// Main menu controller. Reads user input (IP, session name, game mode)
/// and stores it in static fields so the Game scene can access it.
public class MainMenuManager : MonoBehaviour
{
    // =========================================================================
    // STATIC STATE — persists across scene load so Game scene can read it
    // =========================================================================
    public static string ChosenIP = "127.0.0.1";
    public static string ChosenSessionName = "";
    public static ConnectionMode ChosenMode = ConnectionMode.Host;
    public static bool IsSinglePlayer = false;

    public enum ConnectionMode { Host, Client, SinglePlayer }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    [Header("Input Fields")]
    public TMP_InputField sessionNameInput;
    public TMP_InputField ipInputField;

    [Header("Buttons")]
    public Button hostButton;
    public Button joinButton;
    public Button singlePlayerButton;
    public Button howToPlayButton;
    public Button quitButton;
    public Button closeHowToPlayButton;

    [Header("Panels")]
    public GameObject howToPlayPanel;

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    private void Start()
    {
        hostButton.onClick.AddListener(OnHost);
        joinButton.onClick.AddListener(OnJoin);
        singlePlayerButton.onClick.AddListener(OnSinglePlayer);
        howToPlayButton.onClick.AddListener(() => howToPlayPanel.SetActive(true));
        closeHowToPlayButton.onClick.AddListener(() => howToPlayPanel.SetActive(false));
        quitButton.onClick.AddListener(OnQuit);
    }

    // =========================================================================
    // BUTTON HANDLERS
    // =========================================================================

    private void OnHost()
    {
        ChosenMode = ConnectionMode.Host;
        ChosenSessionName = sessionNameInput.text.Trim();
        IsSinglePlayer = false;
        SceneManager.LoadScene("Game");
    }

    private void OnJoin()
    {
        ChosenMode = ConnectionMode.Client;
        string ip = ipInputField.text.Trim();
        // Validate: only allow IP-like strings (digits and dots)
        if (string.IsNullOrEmpty(ip))
            ip = "127.0.0.1";
        ChosenIP = ip;
        ChosenSessionName = sessionNameInput.text.Trim();
        IsSinglePlayer = false;
        SceneManager.LoadScene("Game");
    }

    private void OnSinglePlayer()
    {
        ChosenMode = ConnectionMode.SinglePlayer;
        IsSinglePlayer = true;
        ChosenSessionName = "Single Player";
        SceneManager.LoadScene("Game");
    }

    private void OnQuit()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }
}