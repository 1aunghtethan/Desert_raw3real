using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("AudioManager");
                _instance = go.AddComponent<AudioManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    public static AudioManager Current => _instance;

    public AudioClip pickupStick;
    public AudioClip pickupHay;
    public AudioClip pickupStone;
    public AudioClip footstepWalk;
    public AudioClip footstepRun;
    public AudioClip landingImpact;
    public AudioClip rawKnifeThrow;
    public AudioClip spearThrow;
    public AudioClip stoneThrow;
    public AudioClip afterCraft;
    public AudioClip inventoryOpen;
    public AudioClip weaponSwing;
    public AudioClip weaponHit;
    public AudioClip reduceHeart;
    public AudioClip heartbeat;
    public AudioClip sandFlow;
    public float ReduceHeartSoundCooldown = 3f;

    private AudioSource _sfxSource;
    private AudioSource _stepSource;
    private AudioSource _heartbeatSource;
    private Coroutine _heartbeatRoutine;
    private float _nextStepTime;
    private float _nextReduceHeartSoundTime;

    void Awake()
    {
        if (_instance == null) _instance = this;

        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.playOnAwake = false;
        _sfxSource.spatialBlend = 0f;
        _sfxSource.volume = 1f;

        _stepSource = gameObject.AddComponent<AudioSource>();
        _stepSource.playOnAwake = false;
        _stepSource.spatialBlend = 0f;
        _stepSource.volume = 1f;

        _heartbeatSource = gameObject.AddComponent<AudioSource>();
        _heartbeatSource.playOnAwake = false;
        _heartbeatSource.spatialBlend = 0f;
        _heartbeatSource.loop = true;
        _heartbeatSource.volume = 1f;

        LoadClips();
    }

    private void LoadClips()
    {
        pickupStick = Resources.Load<AudioClip>("Sounds/Hand picking up a dry stick (mp3cut.net)");
        pickupHay = Resources.Load<AudioClip>("Sounds/dry_grass,_with_asmr_#2-1779002734108 (mp3cut.net)");
        pickupStone = Resources.Load<AudioClip>("Sounds/stone pickup");
        footstepWalk = Resources.Load<AudioClip>("Sounds/normal walk footsteps on dry desert sand. (mp3cut.net) (1)");
        footstepRun = Resources.Load<AudioClip>("Sounds/run footsteps on dry desert sand. (mp3cut.net) (1)");
        landingImpact = Resources.Load<AudioClip>("Sounds/Impactful_landing_th_#3-1779001422784");
        rawKnifeThrow = Resources.Load<AudioClip>("Sounds/rawKnifeThrow");
        spearThrow = Resources.Load<AudioClip>("Sounds/spear thrown");
        stoneThrow = Resources.Load<AudioClip>("Sounds/stoneThrow");
        afterCraft = Resources.Load<AudioClip>("Sounds/after craft");
        inventoryOpen = Resources.Load<AudioClip>("Sounds/inventory_opening_wi_#1-1779001860254(1) (mp3cut.net) (1)");
        weaponSwing = Resources.Load<AudioClip>("Sounds/Swift a dagger that made of stone whoosh through a (mp3cut.net)");
        weaponHit = Resources.Load<AudioClip>("Sounds/Wet tearing, one sound of a rough stone dagger sli (mp3cut.net)");
        reduceHeart = Resources.Load<AudioClip>("Sounds/reduce heart");
        heartbeat = Resources.Load<AudioClip>("Sounds/heartbeat");
        sandFlow = Resources.Load<AudioClip>("Sounds/sand_fall_#2-1779329339923");

        if (pickupStick == null) Debug.LogWarning("[AudioManager] Missing: pickupStick");
        if (pickupHay == null) Debug.LogWarning("[AudioManager] Missing: pickupHay");
        if (pickupStone == null) Debug.LogWarning("[AudioManager] Missing: pickupStone");
        if (footstepWalk == null) Debug.LogWarning("[AudioManager] Missing: footstepWalk");
        if (footstepRun == null) Debug.LogWarning("[AudioManager] Missing: footstepRun");
        if (landingImpact == null) Debug.LogWarning("[AudioManager] Missing: landingImpact");
        if (rawKnifeThrow == null) Debug.LogWarning("[AudioManager] Missing: rawKnifeThrow");
        if (spearThrow == null) Debug.LogWarning("[AudioManager] Missing: spearThrow");
        if (stoneThrow == null) Debug.LogWarning("[AudioManager] Missing: stoneThrow");
        if (afterCraft == null) Debug.LogWarning("[AudioManager] Missing: afterCraft");
        if (inventoryOpen == null) Debug.LogWarning("[AudioManager] Missing: inventoryOpen");
        if (weaponSwing == null) Debug.LogWarning("[AudioManager] Missing: weaponSwing");
        if (weaponHit == null) Debug.LogWarning("[AudioManager] Missing: weaponHit");
        if (reduceHeart == null) Debug.LogWarning("[AudioManager] Missing: reduceHeart");
        if (heartbeat == null) Debug.LogWarning("[AudioManager] Missing: heartbeat");
        if (sandFlow == null) Debug.LogWarning("[AudioManager] Missing: sandFlow");
    }

    public void PlaySFX(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        _sfxSource.PlayOneShot(clip, volume);
    }

    public void PlayFootstep(bool isRunning)
    {
        AudioClip clip = isRunning ? footstepRun : footstepWalk;
        if (clip == null) return;

        float interval = isRunning ? 0.3f : 0.5f;
        if (Time.time < _nextStepTime) return;

        _stepSource.Stop();
        _stepSource.clip = clip;
        _stepSource.pitch = Random.Range(0.9f, 1.1f);
        _stepSource.Play();

        _nextStepTime = Time.time + interval;
    }

    public void StopFootsteps()
    {
        _nextStepTime = 0f;

        if (_stepSource != null && _stepSource.isPlaying)
            _stepSource.Stop();
    }

    public void PlayPickupSound(string itemName)
    {
        if (itemName == "Small Branch")
            PlaySFX(pickupStick);
        else if (itemName == "Stonemini")
            PlaySFX(pickupStone);
        else if (itemName == "Real Grass")
            PlaySFX(pickupHay);
    }

    public void PlayThrowSound(string itemName)
    {
        if (itemName == "Raw Knife")
            PlaySFX(rawKnifeThrow);
        else if (itemName == "Stone Spear")
            PlaySFX(spearThrow);
        else if (itemName == "Stonemini")
            PlaySFX(stoneThrow);
    }

    public void PlayCraftSound()
    {
        PlaySFX(afterCraft);
    }

    public void PlayInventoryOpenSound()
    {
        PlaySFX(inventoryOpen);
    }

    public void PlaySandFlowAt(Vector3 position, float volume = 0.7f)
    {
        if (sandFlow == null) return;

        GameObject soundObject = new GameObject("SandFlowSound");
        soundObject.transform.position = position;

        AudioSource source = soundObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.loop = false;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        source.maxDistance = 10f;
        source.volume = Mathf.Clamp01(volume);
        source.clip = sandFlow;
        source.Play();

        Destroy(soundObject, sandFlow.length + 0.1f);
    }

    public void PlayReduceHeartSound()
    {
        if (Time.time < _nextReduceHeartSoundTime) return;

        _nextReduceHeartSoundTime = Time.time + Mathf.Max(0f, ReduceHeartSoundCooldown);
        PlaySFX(reduceHeart);
    }

    public void PlayHeartbeatLoop(float totalSeconds = 12f, float fadeOutSeconds = 3f, float volume = 1f)
    {
        if (heartbeat == null) return;

        if (_heartbeatRoutine != null)
            StopCoroutine(_heartbeatRoutine);

        totalSeconds = Mathf.Max(0f, totalSeconds);
        _heartbeatRoutine = StartCoroutine(PlayHeartbeatLoopRoutine(
            totalSeconds,
            Mathf.Min(Mathf.Max(0f, fadeOutSeconds), totalSeconds),
            Mathf.Clamp01(volume)));
    }

    public void StopHeartbeat()
    {
        if (_heartbeatRoutine != null)
        {
            StopCoroutine(_heartbeatRoutine);
            _heartbeatRoutine = null;
        }

        if (_heartbeatSource != null)
        {
            _heartbeatSource.Stop();
            _heartbeatSource.clip = null;
        }
    }

    private IEnumerator PlayHeartbeatLoopRoutine(float totalSeconds, float fadeOutSeconds, float volume)
    {
        _heartbeatSource.Stop();
        _heartbeatSource.clip = heartbeat;
        _heartbeatSource.loop = true;
        _heartbeatSource.volume = volume;
        _heartbeatSource.Play();

        float holdSeconds = Mathf.Max(0f, totalSeconds - fadeOutSeconds);
        if (holdSeconds > 0f)
            yield return new WaitForSeconds(holdSeconds);

        if (fadeOutSeconds > 0f)
        {
            float elapsed = 0f;
            while (elapsed < fadeOutSeconds)
            {
                elapsed += Time.deltaTime;
                float fade = Mathf.Clamp01(elapsed / fadeOutSeconds);
                _heartbeatSource.volume = Mathf.Lerp(volume, 0f, fade);
                yield return null;
            }
        }

        _heartbeatSource.Stop();
        _heartbeatSource.clip = null;
        _heartbeatSource.volume = volume;
        _heartbeatRoutine = null;
    }
}
