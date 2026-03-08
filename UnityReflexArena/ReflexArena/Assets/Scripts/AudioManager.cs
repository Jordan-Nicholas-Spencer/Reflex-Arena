// =============================================================================
// Attached to: An "AudioManager" GameObject
// Purpose: Centralized sound effect playback. Any script can call
//          AudioManager.Instance.Play___() to trigger a sound.
//          Not a NetworkBehaviour — sounds play locally only.
// =============================================================================

using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("UI Sounds")]
    public AudioClip buttonClick;
    public AudioClip buttonHover;

    [Header("Weapon Sounds")]
    public AudioClip shoot;
    public AudioClip bulletReload;
    public AudioClip emptyGun;

    [Header("Ability Sounds")]
    public AudioClip pocketSandThrow;
    public AudioClip pocketSandHit;
    public AudioClip jamSend;
    public AudioClip jamReceive;

    [Header("Gameplay Sounds")]
    public AudioClip targetHit;
    public AudioClip countdownBeep;
    public AudioClip roundStart;

    [Header("Settings")]
    [Range(0f, 1f)]
    public float sfxVolume = 0.7f;

    private AudioSource audioSource;

    private void Awake()
    {
        // Singleton that persists across scenes
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    /// <summary>Play a clip at the configured volume.</summary>
    private void Play(AudioClip clip)
    {
        if (clip != null)
            audioSource.PlayOneShot(clip, sfxVolume);
    }

    // === Public methods — call these from other scripts ===
    public void PlayButtonClick()      => Play(buttonClick);
    public void PlayButtonHover()      => Play(buttonHover);
    public void PlayShoot()            => Play(shoot);
    public void PlayBulletReload()     => Play(bulletReload);
    public void PlayEmptyGun()         => Play(emptyGun);
    public void PlaySandThrow()        => Play(pocketSandThrow);
    public void PlaySandHit()          => Play(pocketSandHit);
    public void PlayJamSend()          => Play(jamSend);
    public void PlayJamReceive()       => Play(jamReceive);
    public void PlayTargetHit()        => Play(targetHit);
    public void PlayCountdownBeep()    => Play(countdownBeep);
    public void PlayRoundStart()       => Play(roundStart);
}