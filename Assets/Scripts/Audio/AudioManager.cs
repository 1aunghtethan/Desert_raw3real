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

    private AudioSource _sfxSource;
    private AudioSource _stepSource;
    private float _nextStepTime;

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

    public void PlayReduceHeartSound()
    {
        PlaySFX(reduceHeart);
    }
}
