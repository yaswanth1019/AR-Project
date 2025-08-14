using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class TTSManager : MonoBehaviour
{
    [Header("TTS Settings")]
    [SerializeField] private string serverUrl = "https://tts-xzqi.onrender.com/tts";
    [SerializeField] private float volume = 1.0f;
    [SerializeField] private bool use3DAudio = true;
    [SerializeField] private string language = "en";
    [SerializeField] private float speechSpeed = 1.0f;
    [SerializeField] private int requestTimeout = 30; // Increased timeout for TTS
    
    [Header("Audio Source")]
    [SerializeField] private AudioSource audioSource;
    
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;
    
    private Coroutine currentSpeakingCoroutine = null;
    private AudioClip currentClip = null;
    
    private void Awake()
    {
        // Create AudioSource if not assigned
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = use3DAudio ? 1.0f : 0.0f; // 3D vs 2D audio
            audioSource.volume = volume;
            audioSource.pitch = 1.0f;
            
            if (enableDebugLogs)
                Debug.Log("TTSManager: Created AudioSource component");
        }
        else
        {
            // Configure existing AudioSource
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = use3DAudio ? 1.0f : 0.0f;
            audioSource.volume = volume;
        }
    }

    public IEnumerator Speak(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (enableDebugLogs)
                Debug.LogWarning("TTSManager: Empty text provided");
            yield break;
        }

        // Clean up any previous speech
        StopSpeaking();

        if (enableDebugLogs)
            Debug.Log($"TTSManager: Converting text to speech: '{text.Substring(0, Mathf.Min(text.Length, 50))}...'");

        var payload = JsonUtility.ToJson(new TextPayload 
        { 
            text = text,
            lang = language,
            speed = speechSpeed
        });
        
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(payload);

        using (var request = new UnityWebRequest(serverUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "audio/wav");
            request.timeout = requestTimeout;

            if (enableDebugLogs)
                Debug.Log($"TTSManager: Sending request to {serverUrl}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"TTSManager: TTS request failed - {request.error}");
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
                {
                    Debug.LogError($"TTSManager: Server response: {request.downloadHandler.text}");
                }
                yield break;
            }

            var audioData = request.downloadHandler.data;
            
            if (audioData == null || audioData.Length == 0)
            {
                Debug.LogError("TTSManager: Received empty audio data from server");
                yield break;
            }

            if (enableDebugLogs)
                Debug.Log($"TTSManager: Received {audioData.Length} bytes of audio data");

            AudioClip clip = null;

            // Check if response is WAV based on header
            if (IsWavFile(audioData))
            {
                try
                {
                    clip = WavUtility.ToAudioClip(audioData, 0, $"tts_{text.GetHashCode()}");
                    
                    if (clip == null)
                    {
                        Debug.LogError("TTSManager: WavUtility returned null AudioClip");
                        yield break;
                    }
                    
                    if (enableDebugLogs)
                        Debug.Log($"TTSManager: Created AudioClip - Length: {clip.length}s, Channels: {clip.channels}, Frequency: {clip.frequency}Hz");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"TTSManager: Error creating AudioClip: {e.Message}");
                    yield break;
                }
            }
            else
            {
                Debug.LogError("TTSManager: Server returned non-WAV audio format. Expected WAV format.");
                if (enableDebugLogs && audioData.Length >= 4)
                {
                    Debug.LogError($"TTSManager: Received header: {(char)audioData[0]}{(char)audioData[1]}{(char)audioData[2]}{(char)audioData[3]}");
                }
                yield break;
            }

            if (clip != null)
            {
                // Store reference to current clip for cleanup
                currentClip = clip;
                
                // Configure and play audio
                audioSource.clip = clip;
                audioSource.Play();

                if (enableDebugLogs)
                    Debug.Log($"TTSManager: Started playing TTS audio (duration: {clip.length}s)");

                // Wait for audio to finish playing or be stopped
                yield return new WaitWhile(() => audioSource.isPlaying && audioSource.clip == clip);

                if (enableDebugLogs)
                    Debug.Log("TTSManager: Finished playing TTS audio");

                // Clean up the clip
                CleanupCurrentClip();
            }
        }
    }

    private bool IsWavFile(byte[] data)
    {
        if (data == null || data.Length < 4) return false;
        return data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F';
    }

    // Stop current TTS playback
    public void StopSpeaking()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            if (enableDebugLogs)
                Debug.Log("TTSManager: Stopped current TTS playback");
        }
        
        // Stop any running coroutine
        if (currentSpeakingCoroutine != null)
        {
            StopCoroutine(currentSpeakingCoroutine);
            currentSpeakingCoroutine = null;
        }
        
        CleanupCurrentClip();
    }

    // Check if TTS is currently speaking
    public bool IsSpeaking()
    {
        return audioSource != null && audioSource.isPlaying;
    }

    // Set volume
    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (audioSource != null)
        {
            audioSource.volume = volume;
        }
        
        if (enableDebugLogs)
            Debug.Log($"TTSManager: Volume set to {volume}");
    }

    // Set language
    public void SetLanguage(string newLanguage)
    {
        language = newLanguage;
        if (enableDebugLogs)
            Debug.Log($"TTSManager: Language set to {language}");
    }

    // Set speech speed
    public void SetSpeechSpeed(float newSpeed)
    {
        speechSpeed = Mathf.Clamp(newSpeed, 0.1f, 3.0f);
        if (enableDebugLogs)
            Debug.Log($"TTSManager: Speech speed set to {speechSpeed}");
    }

    // Update server URL at runtime
    public void SetServerUrl(string newUrl)
    {
        if (string.IsNullOrEmpty(newUrl))
        {
            Debug.LogError("TTSManager: Cannot set empty server URL");
            return;
        }
        
        serverUrl = newUrl;
        if (enableDebugLogs)
            Debug.Log($"TTSManager: Server URL updated to {serverUrl}");
    }

    // Set 3D audio mode
    public void Set3DAudio(bool enabled)
    {
        use3DAudio = enabled;
        if (audioSource != null)
        {
            audioSource.spatialBlend = use3DAudio ? 1.0f : 0.0f;
        }
        
        if (enableDebugLogs)
            Debug.Log($"TTSManager: 3D Audio set to {use3DAudio}");
    }

    // Get current audio clip length
    public float GetCurrentClipLength()
    {
        if (currentClip != null)
        {
            return currentClip.length;
        }
        return 0f;
    }

    // Get current playback time
    public float GetCurrentPlaybackTime()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            return audioSource.time;
        }
        return 0f;
    }

    // Clean up current clip
    private void CleanupCurrentClip()
    {
        if (currentClip != null)
        {
            if (audioSource != null && audioSource.clip == currentClip)
            {
                audioSource.clip = null;
            }
            DestroyImmediate(currentClip);
            currentClip = null;
        }
    }

    // Public method to start speaking with coroutine tracking
    public void StartSpeaking(string text, System.Action onComplete = null)
    {
        if (currentSpeakingCoroutine != null)
        {
            StopCoroutine(currentSpeakingCoroutine);
        }
        
        currentSpeakingCoroutine = StartCoroutine(SpeakWithCallback(text, onComplete));
    }

    private IEnumerator SpeakWithCallback(string text, System.Action onComplete)
    {
        yield return StartCoroutine(Speak(text));
        currentSpeakingCoroutine = null;
        onComplete?.Invoke();
    }

    private void OnDestroy()
    {
        StopSpeaking();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopSpeaking();
        }
    }

    [System.Serializable]
    private class TextPayload 
    { 
        public string text; 
        public string lang = "en";
        public float speed = 1.0f;
    }
}