// Enhanced Voice UI Controller with better pipeline management
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.Networking;

public class VoiceUIButtonController : MonoBehaviour
{
    [Header("Voice Components")]
    public VoiceInput voiceInput;
    public TTSManager ttsManager;

    [Header("UI Components")]
    public GameObject messagePrefab;
    public Transform contentParent;
    public ScrollRect scrollRect;
    public Button micButton;
    public TextMeshProUGUI statusText;

    [Header("API Settings")]
    [SerializeField] private string chatbotUrl = "http://localhost:8000/chat";
    [SerializeField] private float requestTimeout = 30f;

    private bool isRecording = false;
    private bool isProcessing = false;
    
    private void Start()
    {
        UpdateUI();
    }

    public void OnMicButtonPressed()
    {
        if (isProcessing)
        {
            Debug.Log("⏳ Still processing previous request...");
            return;
        }
        
        if (!isRecording)
        {
            StartVoiceInput();
        }
        else
        {
            StopVoiceInput();
        }
    }
    
    private void StartVoiceInput()
    {
        voiceInput.StartRecording();
        isRecording = true;
        UpdateUI();
        Debug.Log("🔴 Recording started...");
    }
    
    private void StopVoiceInput()
    {
        isRecording = false;
        isProcessing = true;
        UpdateUI();
        
        voiceInput.StopAndRecognize(OnTranscriptionResult);
        Debug.Log("⏹️ Recording stopped, processing...");
    }

    private void OnTranscriptionResult(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.Log("❌ No speech recognized");
            SpawnMessage("System: No speech detected", Color.yellow);
            ResetProcessing();
            return;
        }

        Debug.Log("🗣️ Recognized: " + text);
        SpawnMessage("You: " + text, Color.cyan);
        
        // Send to chatbot
        StartCoroutine(SendToChatbot(text));
    }

    private IEnumerator SendToChatbot(string userText)
    {
        UpdateStatus("Thinking...");
        
        ChatRequest payload = new ChatRequest { question = userText };
        string json = JsonUtility.ToJson(payload);

        using (UnityWebRequest request = new UnityWebRequest(chatbotUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = (int)requestTimeout;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Chatbot request failed: {request.error}");
                string errorMsg = "Sorry, I couldn't process your request.";
                SpawnMessage("Bot: " + errorMsg, Color.red);
                
                // Still try to speak the error message
                StartCoroutine(SpeakResponse(errorMsg));
            }
            else
            {
                try
                {
                    string responseJson = request.downloadHandler.text;
                    if (string.IsNullOrEmpty(responseJson))
                    {
                        throw new System.Exception("Empty response from chatbot");
                    }
                    
                    string reply = ExtractReply(responseJson);
                    
                    if (string.IsNullOrEmpty(reply))
                    {
                        throw new System.Exception("Empty reply from chatbot");
                    }
                    
                    Debug.Log("🤖 Bot response: " + reply);
                    SpawnMessage("Bot: " + reply, Color.white);
                    
                    // Convert to speech
                    StartCoroutine(SpeakResponse(reply));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error processing chatbot response: {e.Message}");
                    string errorMsg = "Sorry, I had trouble understanding the response.";
                    SpawnMessage("Bot: " + errorMsg, Color.red);
                    StartCoroutine(SpeakResponse(errorMsg));
                }
            }
        }
    }
    
    private IEnumerator SpeakResponse(string text)
    {
        UpdateStatus("Speaking...");
        
        // Stop any current TTS
        if (ttsManager.IsSpeaking())
        {
            ttsManager.StopSpeaking();
        }
        
        yield return StartCoroutine(ttsManager.Speak(text));
        
        ResetProcessing();
    }
    
    private void ResetProcessing()
    {
        isProcessing = false;
        UpdateUI();
    }
    
    private void UpdateUI()
    {
        if (statusText != null)
        {
            if (isRecording)
            {
                statusText.text = "🔴 Recording...";
                statusText.color = Color.red;
            }
            else if (isProcessing)
            {
                statusText.text = "⏳ Processing...";
                statusText.color = Color.yellow;
            }
            else
            {
                statusText.text = "🎤 Ready";
                statusText.color = Color.green;
            }
        }
        
        if (micButton != null)
        {
            micButton.interactable = !isProcessing;
        }
    }
    
    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SpawnMessage(string message, Color color)
    {
        if (messagePrefab != null && contentParent != null)
        {
            var go = Instantiate(messagePrefab, contentParent);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = message;
                tmp.color = color;
            }

            // Force UI update and scroll to bottom
            Canvas.ForceUpdateCanvases();
            
            if (scrollRect != null)
            {
                StartCoroutine(ScrollToBottom());
            }
        }
    }
    
    private IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private string ExtractReply(string json)
    {
        try
        {
            var response = JsonUtility.FromJson<BotResponse>(json);
            return response?.response ?? "";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing bot response: {e.Message}");
            return "";
        }
    }
    
    public void SetChatbotUrl(string newUrl)
    {
        chatbotUrl = newUrl;
    }
    
    // Manual stop function (can be called by UI button)
    public void StopAll()
    {
        if (isRecording)
        {
            voiceInput.StopListening();
            isRecording = false;
        }
        
        if (ttsManager.IsSpeaking())
        {
            ttsManager.StopSpeaking();
        }
        
        // Stop all coroutines
        StopAllCoroutines();
        
        ResetProcessing();
        Debug.Log("🛑 All voice processes stopped");
    }

    private void OnDestroy()
    {
        StopAll();
    }

    [System.Serializable]
    public class ChatRequest
    {
        public string question;
    }

    [System.Serializable]
    public class BotResponse
    {
        public string response;
    }
}