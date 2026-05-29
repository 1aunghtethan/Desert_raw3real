using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class IntroMusicController : MonoBehaviour
{
    [SerializeField] private string firstTrackResourcePath = "Sounds/background/last_grain_of_sand";
    [SerializeField] private string secondTrackResourcePath = "Sounds/background/under_an_empty_sun";
    [SerializeField, Range(0f, 1f)] private float volume = 0.55f;
    [SerializeField] private float fadeSeconds = 2f;
    [SerializeField] private float secondsBetweenTracks = 3f;

    private AudioSource musicSource;
    private AudioClip[] playlist;
    private Coroutine playlistRoutine;
    private Coroutine fadeOutRoutine;

    private void Awake()
    {
        musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
            musicSource = gameObject.AddComponent<AudioSource>();

        musicSource.playOnAwake = false;
        musicSource.loop = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = 0f;

        playlist = new[]
        {
            Resources.Load<AudioClip>(firstTrackResourcePath),
            Resources.Load<AudioClip>(secondTrackResourcePath)
        };
    }

    private void OnEnable()
    {
        StartPlaylist();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        playlistRoutine = null;
        fadeOutRoutine = null;

        if (musicSource != null)
            musicSource.Stop();
    }

    public void StartPlaylist()
    {
        if (playlistRoutine == null && HasPlayableTrack())
            playlistRoutine = StartCoroutine(PlayPlaylistLoop());
    }

    public void FadeOutAndStop()
    {
        if (!isActiveAndEnabled || musicSource == null)
            return;

        if (playlistRoutine != null)
        {
            StopCoroutine(playlistRoutine);
            playlistRoutine = null;
        }

        if (fadeOutRoutine != null)
            StopCoroutine(fadeOutRoutine);

        fadeOutRoutine = StartCoroutine(FadeOutAndStopRoutine());
    }

    private IEnumerator PlayPlaylistLoop()
    {
        int trackIndex = 0;

        while (true)
        {
            AudioClip clip = GetNextPlayableClip(ref trackIndex);
            if (clip == null)
                yield break;

            musicSource.clip = clip;
            musicSource.volume = 0f;
            musicSource.Play();

            yield return FadeVolume(0f, volume, fadeSeconds);

            float holdSeconds = Mathf.Max(0f, clip.length - (fadeSeconds * 2f));
            yield return WaitUnscaled(holdSeconds);

            yield return FadeVolume(musicSource.volume, 0f, fadeSeconds);
            musicSource.Stop();
            musicSource.clip = null;

            yield return WaitUnscaled(secondsBetweenTracks);
        }
    }

    private AudioClip GetNextPlayableClip(ref int trackIndex)
    {
        if (playlist == null || playlist.Length == 0)
            return null;

        for (int i = 0; i < playlist.Length; i++)
        {
            AudioClip clip = playlist[trackIndex % playlist.Length];
            trackIndex++;

            if (clip != null)
                return clip;
        }

        Debug.LogWarning("[IntroMusicController] No intro background music clips were found in Resources.");
        return null;
    }

    private bool HasPlayableTrack()
    {
        if (playlist == null)
            return false;

        foreach (AudioClip clip in playlist)
        {
            if (clip != null)
                return true;
        }

        Debug.LogWarning("[IntroMusicController] Missing intro music clips. Check Resources/Sounds/background.");
        return false;
    }

    private IEnumerator FadeOutAndStopRoutine()
    {
        yield return FadeVolume(musicSource.volume, 0f, fadeSeconds);
        musicSource.Stop();
        musicSource.clip = null;
        fadeOutRoutine = null;
    }

    private IEnumerator FadeVolume(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            musicSource.volume = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        musicSource.volume = to;
    }

    private static IEnumerator WaitUnscaled(float seconds)
    {
        if (seconds <= 0f)
            yield break;

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
