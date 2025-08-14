using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class VoiceInput : MonoBehaviour
{
    [Header("STT Settings")]
    [SerializeField] private string sttServerUrl = "http://localhost:5005/stt";
    [SerializeField] private int recordingLength = 5;
    [SerializeField] private int sampleRate = 44100;
    
    private AudioClip clip;
    private string micDevice;
    private bool isRecording = false;
    
    private void Start()
    {
        // Initialize microphone device
        if (Microphone.devices.Length > 0)
        {
            micDevice = Microphone.devices[0];
            Debug.Log($"Using microphone: {micDevice}");
        }
        else
        {
            Debug.LogError("No microphone devices found!");
        }
    }

    public void StartRecording()
    {
        if (string.IsNullOrEmpty(micDevice))
        {
            Debug.LogError("No microphone device available");
            return;
        }
        
        if (isRecording)
        {
            Debug.LogWarning("Already recording");
            return;
        }
        
        // Clean up previous clip
        if (clip != null)
        {
            DestroyImmediate(clip);
        }
        
        clip = Microphone.Start(micDevice, false, recordingLength, sampleRate);
        isRecording = true;
        Debug.Log("🔴 Recording started...");
    }

    public void StopAndRecognize(System.Action<string> callback)
    {
        if (!isRecording)
        {
            Debug.LogWarning("Not currently recording");
            callback?.Invoke("");
            return;
        }
        
        Microphone.End(micDevice);
        isRecording = false;
        Debug.Log("⏹️ Recording stopped");
        
        if (clip != null)
        {
            StartCoroutine(SendToSTT(callback));
        }
        else
        {
            Debug.LogError("No audio clip to process");
            callback?.Invoke("");
        }
    }

    private IEnumerator SendToSTT(System.Action<string> callback)
    {
        if (clip == null)
        {
            Debug.LogError("No audio clip available for STT");
            callback?.Invoke("");
            yield break;
        }
        
        byte[] wavData = null;
        
        try
        {
            // Trim audio to actual recorded length
            AudioClip trimmedClip = TrimAudioClip(clip);
            wavData = WavUtility.FromAudioClip(trimmedClip);
            
            if (trimmedClip != clip)
            {
                DestroyImmediate(trimmedClip);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error processing audio clip: {e.Message}");
            callback?.Invoke("");
            yield break;
        }
        
        if (wavData == null || wavData.Length == 0)
        {
            Debug.LogError("Failed to convert audio to WAV data");
            callback?.Invoke("");
            yield break;
        }
        
        Debug.Log($"Sending {wavData.Length} bytes to STT server");

        using (UnityWebRequest www = new UnityWebRequest(sttServerUrl, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(wavData);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "audio/wav");
            www.timeout = 10;

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"STT Failed: {www.error}");
                if (!string.IsNullOrEmpty(www.downloadHandler.text))
                {
                    Debug.LogError($"Server response: {www.downloadHandler.text}");
                }
                callback?.Invoke("");
            }
            else
            {
                try
                {
                    string json = www.downloadHandler.text;
                    if (string.IsNullOrEmpty(json))
                    {
                        Debug.LogError("Empty response from STT server");
                        callback?.Invoke("");
                    }
                    else
                    {
                        var response = JsonUtility.FromJson<ResponseText>(json);
                        string recognizedText = response?.text ?? "";
                        Debug.Log($"🗣️ STT Result: '{recognizedText}'");
                        callback?.Invoke(recognizedText);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error parsing STT response: {e.Message}");
                    callback?.Invoke("");
                }
            }
        }
        
        // Clean up
        if (clip != null)
        {
            DestroyImmediate(clip);
            clip = null;
        }
    }
    
    private AudioClip TrimAudioClip(AudioClip originalClip)
    {
        if (originalClip == null) return null;
        
        int microphonePosition = Microphone.GetPosition(micDevice);
        if (microphonePosition <= 0) return originalClip;
        
        float[] samples = new float[microphonePosition * originalClip.channels];
        originalClip.GetData(samples, 0);
        
        AudioClip trimmedClip = AudioClip.Create(
            originalClip.name + "_trimmed",
            microphonePosition,
            originalClip.channels,
            originalClip.frequency,
            false
        );
        
        trimmedClip.SetData(samples, 0);
        return trimmedClip;
    }

    public void StopListening()
    {
        if (Microphone.IsRecording(micDevice))
        {
            Microphone.End(micDevice);
            Debug.Log("Microphone recording stopped.");
        }
        
        isRecording = false;
        
        if (clip != null)
        {
            DestroyImmediate(clip);
            clip = null;
        }
    }
    
    public bool IsRecording()
    {
        return isRecording && Microphone.IsRecording(micDevice);
    }
    
    public void SetServerUrl(string newUrl)
    {
        sttServerUrl = newUrl;
    }

    private void OnDestroy()
    {
        StopListening();
    }

    [System.Serializable]
    public class ResponseText
    {
        public string text;
    }
}