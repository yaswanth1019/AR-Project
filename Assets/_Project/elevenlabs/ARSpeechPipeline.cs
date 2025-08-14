using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using TMPro;
using System.IO;
using System;

// Data structures for API responses
[System.Serializable]
public class BackendResponse
{
    public bool success;
    public string error;
    public ResponseData data;
    public PerformanceData performance;
    public MetadataInfo metadata;
}

[System.Serializable]
public class ResponseData
{
    public string transcription;
    public string aiResponse;
    public string audioUrl;
}

[System.Serializable]
public class PerformanceData
{
    public int total_time;
    public int stt_time;
    public int rag_time;
    public int tts_time;
    public int stt_rag_time; // For backwards compatibility
}

[System.Serializable]
public class MetadataInfo
{
    public string stt_engine;
    public string rag_engine;
    public string tts_engine;
    public string timestamp;
}

[System.Serializable]
public class HealthResponse
{
    public string status;
    public string message;
    public string timestamp;
}

[System.Serializable]
public class SystemCheckResponse
{
    public bool system_ready;
    public string[] recommendations;
    public string status;
}

public class ARSpeechPipeline : MonoBehaviour
{
    [Header("Backend Settings")]
    public string backendUrl = "http://localhost:3000"; // Your FastAPI server URL
    
    [Header("UI Components")]
    public Button recordButton;
    public TMP_Text statusText;
    public TMP_Text conversationText;
    public AudioSource audioSource;
    public AudioSource microphoneSource;

    [Header("Button Visual Feedback")]
    public Image buttonImage;
    public Color normalColor = Color.white;
    public Color recordingColor = Color.red;
    public Color processingColor = Color.yellow;
    public Animator buttonAnimator;

    [Header("Recording Settings")]
    public float recordingTime = 10f;
    public int sampleRate = 16000; // Match server expectation

    private bool isRecording = false;
    private bool isProcessing = false;
    private AudioClip recordedClip;
    private string deviceName;
    private Coroutine statusUpdateCoroutine;

    private void Start()
    {
        InitializeMicrophone();
        SetupButton();
        UpdateStatus("Ready to record. Hold button to speak.");
        
        // Test backend connection on start
        StartCoroutine(TestConnection());
    }

    private void InitializeMicrophone()
    {
        if (Microphone.devices.Length > 0)
        {
            deviceName = Microphone.devices[0];
            Debug.Log($"Using microphone: {deviceName}");
        }
        else
        {
            UpdateStatus("No microphone detected!");
        }
    }

    private void SetupButton()
    {
        if (recordButton != null)
        {
            EventTrigger eventTrigger = recordButton.GetComponent<EventTrigger>();
            if (eventTrigger == null)
            {
                eventTrigger = recordButton.gameObject.AddComponent<EventTrigger>();
            }

            eventTrigger.triggers.Clear();

            EventTrigger.Entry pointerDownEntry = new EventTrigger.Entry();
            pointerDownEntry.eventID = EventTriggerType.PointerDown;
            pointerDownEntry.callback.AddListener((data) => { OnButtonPressed(); });
            eventTrigger.triggers.Add(pointerDownEntry);

            EventTrigger.Entry pointerUpEntry = new EventTrigger.Entry();
            pointerUpEntry.eventID = EventTriggerType.PointerUp;
            pointerUpEntry.callback.AddListener((data) => { OnButtonReleased(); });
            eventTrigger.triggers.Add(pointerUpEntry);

            EventTrigger.Entry pointerExitEntry = new EventTrigger.Entry();
            pointerExitEntry.eventID = EventTriggerType.PointerExit;
            pointerExitEntry.callback.AddListener((data) => { OnButtonReleased(); });
            eventTrigger.triggers.Add(pointerExitEntry);

            if (buttonImage != null)
            {
                buttonImage.color = normalColor;
            }
        }
    }

    private void OnButtonPressed()
    {
        if (!isProcessing)
        {
            StartRecording();
        }
    }

    private void OnButtonReleased()
    {
        if (isRecording)
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        if (isRecording) return;

        isRecording = true;
        UpdateStatus("Recording... Release button when done.");

        if (buttonImage != null)
        {
            buttonImage.color = recordingColor;
        }

        if (buttonAnimator != null)
        {
            buttonAnimator.SetBool("IsRecording", true);
        }

        recordedClip = Microphone.Start(deviceName, false, (int)recordingTime, sampleRate);

        if (microphoneSource != null)
        {
            microphoneSource.clip = recordedClip;
            microphoneSource.loop = true;
            microphoneSource.Play();
        }

#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }

    private void StopRecording()
    {
        if (!isRecording) return;

        isRecording = false;
        isProcessing = true;

        Microphone.End(deviceName);

        if (microphoneSource != null)
        {
            microphoneSource.Stop();
        }

        if (buttonImage != null)
        {
            buttonImage.color = processingColor;
        }

        if (buttonAnimator != null)
        {
            buttonAnimator.SetBool("IsRecording", false);
        }

        // Start processing status updates
        statusUpdateCoroutine = StartCoroutine(ProcessingStatusUpdates());

        // Trim silence and process (remove SavWav dependency)
        AudioClip trimmedClip = TrimSilence(recordedClip, 0.01f);
        StartCoroutine(ProcessSpeechPipeline(trimmedClip));
    }

    private AudioClip TrimSilence(AudioClip audioClip, float threshold)
    {
        if (audioClip == null) return null;
        
        // Simple silence trimming - you can implement more sophisticated trimming if needed
        float[] samples = new float[audioClip.samples * audioClip.channels];
        audioClip.GetData(samples, 0);
        
        // Find first non-silent sample
        int startIndex = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            if (Mathf.Abs(samples[i]) > threshold)
            {
                startIndex = i;
                break;
            }
        }
        
        // Find last non-silent sample
        int endIndex = samples.Length - 1;
        for (int i = samples.Length - 1; i >= 0; i--)
        {
            if (Mathf.Abs(samples[i]) > threshold)
            {
                endIndex = i;
                break;
            }
        }
        
        // If no audio found, return original
        if (startIndex >= endIndex) return audioClip;
        
        // Create trimmed clip
        int trimmedLength = endIndex - startIndex + 1;
        float[] trimmedSamples = new float[trimmedLength];
        System.Array.Copy(samples, startIndex, trimmedSamples, 0, trimmedLength);
        
        AudioClip trimmedClip = AudioClip.Create("TrimmedAudio", 
            trimmedLength / audioClip.channels, 
            audioClip.channels, 
            audioClip.frequency, 
            false);
        
        trimmedClip.SetData(trimmedSamples, 0);
        return trimmedClip;
    }

    private IEnumerator ProcessingStatusUpdates()
    {
        string[] statusMessages = {
            "Processing speech...",
            "Converting speech to text...",
            "Searching knowledge base...",
            "Generating AI response...",
            "Converting response to speech...",
            "Almost done..."
        };

        int currentIndex = 0;
        while (isProcessing)
        {
            UpdateStatus(statusMessages[currentIndex % statusMessages.Length]);
            currentIndex++;
            yield return new WaitForSeconds(2f);
        }
    }

    private IEnumerator ProcessSpeechPipeline(AudioClip audioClip)
    {
        byte[] audioBytes = null;
        try
        {
            audioBytes = ConvertAudioClipToWav(audioClip);
        }
        catch (Exception ex)
        {
            UpdateStatus("Audio conversion error: " + ex.Message);
            FinishProcessing();
            yield break;
        }

        if (audioBytes == null || audioBytes.Length == 0)
        {
            UpdateStatus("Failed to convert audio. Please try again.");
            FinishProcessing();
            yield break;
        }

        yield return StartCoroutine(SendAudioToBackend(audioBytes));
        FinishProcessing();
    }

    private void FinishProcessing()
    {
        isProcessing = false;
        
        if (statusUpdateCoroutine != null)
        {
            StopCoroutine(statusUpdateCoroutine);
            statusUpdateCoroutine = null;
        }

        if (buttonImage != null)
        {
            buttonImage.color = normalColor;
        }

        UpdateStatus("Ready to record. Hold button to speak.");
    }

    // Enhanced version with better error handling and validation
private IEnumerator SendAudioToBackend(byte[] audioBytes)
{
    string endpoint = backendUrl + "/api/speech/process";
    
    // Validate audio data size (add minimum size check)
    if (audioBytes.Length < 1000) // Less than 1KB likely too short
    {
        UpdateStatus("Audio recording too short. Please speak longer.");
        yield break;
    }
    
    // Create form data
    WWWForm form = new WWWForm();
    form.AddBinaryData("audio", audioBytes, "recording.wav", "audio/wav");
    
    UnityWebRequest request = UnityWebRequest.Post(endpoint, form);
    
    // Set timeout to match server expectation
    request.timeout = 120;
    
    // Add headers for better compatibility
    request.SetRequestHeader("Accept", "application/json");
    
    yield return request.SendWebRequest();
    
    if (request.result == UnityWebRequest.Result.Success)
    {
        string responseText = request.downloadHandler.text;
        Debug.Log($"Backend Response: {responseText}");

        BackendResponse response = null;
        try
        {
            response = JsonUtility.FromJson<BackendResponse>(responseText);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing response: {e.Message}");
            Debug.LogError($"Response content: {responseText}");
            UpdateStatus("Failed to parse server response");
            yield break;
        }

        if (response != null && response.success)
        {
            // Validate response data
            if (response.data == null)
            {
                UpdateStatus("Invalid response from server");
                yield break;
            }

            // Check for empty transcription
            if (string.IsNullOrEmpty(response.data.transcription))
            {
                UpdateStatus("No speech detected. Please try again.");
                yield break;
            }

            // Update UI with transcription
            UpdateStatus("You said: " + response.data.transcription);
            AddToConversation("You: " + response.data.transcription);

            // Update with AI response
            if (!string.IsNullOrEmpty(response.data.aiResponse))
            {
                UpdateStatus("AI: " + response.data.aiResponse);
                AddToConversation("AI: " + response.data.aiResponse);
            }

            // Show performance info if available
            if (response.performance != null)
            {
                int sttTime = response.performance.stt_time > 0 ? response.performance.stt_time : response.performance.stt_rag_time;
                Debug.Log($"Performance - Total: {response.performance.total_time}ms, " +
                         $"STT: {sttTime}ms, " +
                         $"RAG: {response.performance.rag_time}ms, " +
                         $"TTS: {response.performance.tts_time}ms");
            }

            // Show metadata if available
            if (response.metadata != null)
            {
                Debug.Log($"Metadata - STT Engine: {response.metadata.stt_engine}, " +
                         $"TTS Engine: {response.metadata.tts_engine}, " +
                         $"Timestamp: {response.metadata.timestamp}");
            }

            // Download and play audio response if URL is provided
            if (!string.IsNullOrEmpty(response.data.audioUrl))
            {
                yield return StartCoroutine(DownloadAndPlayAudio(response.data.audioUrl));
            }
            else
            {
                UpdateStatus("Response received but no audio generated");
            }
        }
        else
        {
            string errorMsg = response != null ? response.error : "Unknown server error";
            UpdateStatus("Server error: " + errorMsg);
            Debug.LogError($"Backend error: {errorMsg}");
        }
    }
    else
    {
        // Handle different types of network errors
        string errorMessage = "";
        switch (request.result)
        {
            case UnityWebRequest.Result.ConnectionError:
                errorMessage = "Connection failed - check server is running";
                break;
            case UnityWebRequest.Result.ProtocolError:
                errorMessage = $"Server error (HTTP {request.responseCode})";
                if (request.responseCode == 503)
                {
                    errorMessage += " - Models not initialized";
                }
                break;
            case UnityWebRequest.Result.DataProcessingError:
                errorMessage = "Data processing error";
                break;
            default:
                errorMessage = request.error;
                break;
        }
        
        Debug.LogError($"Request failed: {errorMessage}");
        UpdateStatus("Connection failed: " + errorMessage);
    }
}

    private IEnumerator DownloadAndPlayAudio(string audioUrl)
    {
        if (string.IsNullOrEmpty(audioUrl))
        {
            Debug.LogWarning("No audio URL provided");
            yield break;
        }
        
        UpdateStatus("Downloading audio response...");
        
        // Construct full URL if it's a relative path
        string fullUrl = audioUrl.StartsWith("http") ? audioUrl : backendUrl + audioUrl;
        
        Debug.Log($"Downloading audio from: {fullUrl}");
        
        UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(fullUrl, AudioType.WAV);
        audioRequest.timeout = 30;
        
        yield return audioRequest.SendWebRequest();
        
        if (audioRequest.result == UnityWebRequest.Result.Success)
        {
            AudioClip clip = DownloadHandlerAudioClip.GetContent(audioRequest);
            
            if (audioSource != null && clip != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
                UpdateStatus("Playing response...");
                
                // Wait for audio to finish playing
                yield return new WaitForSeconds(clip.length);
                UpdateStatus("Ready to record. Hold button to speak.");
            }
            else
            {
                UpdateStatus("Failed to create audio clip");
            }
        }
        else
        {
            Debug.LogError($"Audio download failed: {audioRequest.error}");
            Debug.LogError($"Response Code: {audioRequest.responseCode}");
            UpdateStatus("Failed to download audio response");
        }
    }

    private byte[] ConvertAudioClipToWav(AudioClip audioClip)
    {
        if (audioClip == null) return null;

        // Get audio data
        float[] samples = new float[audioClip.samples * audioClip.channels];
        audioClip.GetData(samples, 0);

        // Convert to WAV format
        return ConvertFloatSamplesToWav(samples, audioClip.frequency, audioClip.channels);
    }

    private byte[] ConvertFloatSamplesToWav(float[] samples, int frequency, int channels)
    {
        using (var memoryStream = new MemoryStream())
        using (var writer = new BinaryWriter(memoryStream))
        {
            int sampleCount = samples.Length;
            int byteRate = frequency * channels * 2; // 16-bit = 2 bytes per sample
            int blockAlign = channels * 2;
            int dataSize = sampleCount * 2;

            // WAV header
            writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
            writer.Write(16); // PCM format size
            writer.Write((short)1); // PCM format
            writer.Write((short)channels);
            writer.Write(frequency);
            writer.Write(byteRate);
            writer.Write((short)blockAlign);
            writer.Write((short)16); // 16-bit

            // Data header
            writer.Write(System.Text.Encoding.UTF8.GetBytes("data"));
            writer.Write(dataSize);

            // Convert float samples to 16-bit PCM
            for (int i = 0; i < samples.Length; i++)
            {
                short sample = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767);
                writer.Write(sample);
            }

            return memoryStream.ToArray();
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"Status: {message}");
    }

    private void AddToConversation(string message)
    {
        if (conversationText != null)
        {
            conversationText.text += message + "\n\n";
            
            // Scroll to bottom if using ScrollRect
            ScrollRect scrollRect = conversationText.transform.parent.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    // Test backend connection
    [ContextMenu("Test Backend Connection")]
    public void TestBackendConnection()
    {
        StartCoroutine(TestConnection());
    }

    private IEnumerator TestConnection()
    {
        UpdateStatus("Testing backend connection...");
        
        string endpoint = backendUrl + "/api/health";
        
        UnityWebRequest request = UnityWebRequest.Get(endpoint);
        request.timeout = 10;
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Backend connection successful!");
            Debug.Log("Health response: " + request.downloadHandler.text);
            
            try
            {
                HealthResponse healthResponse = JsonUtility.FromJson<HealthResponse>(request.downloadHandler.text);
                UpdateStatus($"Backend connected - Status: {healthResponse.status}");
            }
            catch (System.Exception e)
            {
                UpdateStatus("Backend connected successfully");
            }
            
            // Also test system check
            yield return StartCoroutine(TestSystemCheck());
        }
        else
        {
            Debug.LogError($"Backend connection failed: {request.error}");
            UpdateStatus("Backend connection failed: " + request.error);
        }
    }
    
    private IEnumerator TestSystemCheck()
    {
        string endpoint = backendUrl + "/api/system-check";
        
        UnityWebRequest request = UnityWebRequest.Get(endpoint);
        request.timeout = 15;
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("System check response: " + request.downloadHandler.text);
            
            try
            {
                SystemCheckResponse systemCheck = JsonUtility.FromJson<SystemCheckResponse>(request.downloadHandler.text);
                if (systemCheck.system_ready)
                {
                    UpdateStatus("System ready for speech processing");
                }
                else
                {
                    UpdateStatus("System not ready - check server configuration");
                    Debug.LogWarning("System not ready. Check server setup.");
                    
                    // Log recommendations if available
                    if (systemCheck.recommendations != null && systemCheck.recommendations.Length > 0)
                    {
                        Debug.LogWarning("Recommendations:");
                        foreach (string rec in systemCheck.recommendations)
                        {
                            Debug.LogWarning($"- {rec}");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to parse system check: {e.Message}");
            }
        }
        else
        {
            Debug.LogError($"System check failed: {request.error}");
        }
    }

    // Context menu for clearing cache
    [ContextMenu("Clear Server Cache")]
    public void ClearServerCache()
    {
        StartCoroutine(ClearCache());
    }

    private IEnumerator ClearCache()
    {
        string endpoint = backendUrl + "/api/cache/clear";
        
        UnityWebRequest request = UnityWebRequest.Post(endpoint, "");
        request.timeout = 10;
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Cache cleared successfully!");
            UpdateStatus("Server cache cleared");
        }
        else
        {
            Debug.LogError($"Cache clear failed: {request.error}");
            UpdateStatus("Failed to clear cache");
        }
    }

    // Context menu for server cleanup
    [ContextMenu("Cleanup Server Files")]
    public void CleanupServerFiles()
    {
        StartCoroutine(CleanupFiles());
    }

    private IEnumerator CleanupFiles()
    {
        string endpoint = backendUrl + "/api/cleanup";
        
        UnityWebRequest request = UnityWebRequest.Post(endpoint, "");
        request.timeout = 10;
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Server cleanup successful!");
            UpdateStatus("Server files cleaned up");
        }
        else
        {
            Debug.LogError($"Server cleanup failed: {request.error}");
            UpdateStatus("Failed to cleanup server files");
        }
    }
}