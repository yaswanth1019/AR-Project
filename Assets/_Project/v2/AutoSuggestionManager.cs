using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using ARLocation.MapboxRoutes;
using ARLocation;
using System;

public class AutoSuggestionManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField searchInputField;
    public GameObject suggestionPanel;
    public GameObject suggestionItemPrefab;
    public ScrollRect suggestionScrollRect; // Optional: for better UX with many suggestions

    [Header("Mapbox Configuration")]
    public MapboxApi mapboxApi;
    [SerializeField] private string mapboxAccessToken = "your_mapbox_token_here"; // Fallback token

    [Header("Settings")]
    [Range(0.1f, 2f)]
    public float searchDelay = 0.3f; // Reduced delay for better responsiveness
    [Range(3, 10)]
    public int maxSuggestions = 5;
    [Range(2, 5)]
    public int minQueryLength = 2;
    public bool enableCaching = true;
    public int maxCacheSize = 50;

    [Header("Events")]
    public UnityEngine.Events.UnityEvent<GeocodingFeature> OnPlaceSelectedEvent;

    // Private fields
    private Coroutine searchCoroutine;
    private readonly Queue<GameObject> suggestionItemPool = new Queue<GameObject>();
    private readonly List<GameObject> activeSuggestionItems = new List<GameObject>();
    private readonly Dictionary<string, CachedResult> searchCache = new Dictionary<string, CachedResult>();
    private readonly Queue<string> cacheOrder = new Queue<string>();
    
    private string lastSearchQuery = "";
    private bool isSearching = false;
    private GeocodingFeature lastSelectedFeature;

    // Cache structure
    [Serializable]
    private class CachedResult
    {
        public List<GeocodingFeature> features;
        public float timestamp;
        
        public CachedResult(List<GeocodingFeature> features)
        {
            this.features = new List<GeocodingFeature>(features);
            this.timestamp = Time.time;
        }
        
        public bool IsValid(float maxAge = 300f) // 5 minutes default
        {
            return Time.time - timestamp < maxAge;
        }
    }

    #region Unity Lifecycle

    void Start()
    {
        InitializeComponents();
        SetupEventListeners();
        InitializeSuggestionPool();
    }

    void OnDestroy()
    {
        CleanupEventListeners();
        ClearCache();
    }

    #endregion

    #region Initialization

    private void InitializeComponents()
    {
        // Initialize Mapbox API if not set
        if (mapboxApi == null)
        {
            if (!string.IsNullOrEmpty(mapboxAccessToken))
            {
                mapboxApi = new MapboxApi(mapboxAccessToken);
                Debug.Log("[AutoSuggestionManager] Created new MapboxApi instance with fallback token");
            }
            else
            {
                Debug.LogError("[AutoSuggestionManager] MapboxApi is not assigned and no fallback token provided!");
                enabled = false;
                return;
            }
        }
        else
        {
            // Validate existing MapboxApi has a token
            if (string.IsNullOrEmpty(mapboxApi.AccessToken))
            {
                Debug.LogError("[AutoSuggestionManager] MapboxApi is assigned but AccessToken is empty!");
                enabled = false;
                return;
            }
        }

        // Validate required components
        if (searchInputField == null)
        {
            Debug.LogError("[AutoSuggestionManager] SearchInputField is not assigned!");
            enabled = false;
            return;
        }

        if (suggestionPanel == null)
        {
            Debug.LogError("[AutoSuggestionManager] SuggestionPanel is not assigned!");
            enabled = false;
            return;
        }

        if (suggestionItemPrefab == null)
        {
            Debug.LogError("[AutoSuggestionManager] SuggestionItemPrefab is not assigned!");
            enabled = false;
            return;
        }

        // Initially hide suggestion panel
        suggestionPanel.SetActive(false);
    }

    private void SetupEventListeners()
    {
        if (searchInputField != null)
        {
            searchInputField.onValueChanged.AddListener(OnSearchInputChanged);
            searchInputField.onEndEdit.AddListener(OnSearchInputEndEdit);
        }
    }

    private void CleanupEventListeners()
    {
        if (searchInputField != null)
        {
            searchInputField.onValueChanged.RemoveListener(OnSearchInputChanged);
            searchInputField.onEndEdit.RemoveListener(OnSearchInputEndEdit);
        }
    }

    private void InitializeSuggestionPool()
    {
        // Pre-create suggestion items for object pooling
        for (int i = 0; i < maxSuggestions; i++)
        {
            GameObject pooledItem = Instantiate(suggestionItemPrefab, suggestionPanel.transform);
            pooledItem.SetActive(false);
            suggestionItemPool.Enqueue(pooledItem);
        }
    }

    #endregion

    #region Input Handling

    private void OnSearchInputChanged(string query)
    {
        // Stop previous search
        StopCurrentSearch();

        // Trim whitespace
        query = query.Trim();

        // Validate query length
        if (string.IsNullOrEmpty(query) || query.Length < minQueryLength)
        {
            HideSuggestions();
            return;
        }

        // Check cache first
        if (enableCaching && TryGetCachedResult(query, out List<GeocodingFeature> cachedFeatures))
        {
            DisplaySuggestions(cachedFeatures);
            return;
        }

        // Start new search with debouncing
        searchCoroutine = StartCoroutine(SearchWithDelay(query));
    }

    private void OnSearchInputEndEdit(string query)
    {
        // Optional: Handle when user presses Enter
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (activeSuggestionItems.Count > 0)
            {
                // Auto-select first suggestion on Enter
                var firstButton = activeSuggestionItems[0].GetComponent<Button>();
                firstButton?.onClick.Invoke();
            }
        }
    }

    #endregion

    #region Search Logic

    private void StopCurrentSearch()
    {
        if (searchCoroutine != null)
        {
            StopCoroutine(searchCoroutine);
            searchCoroutine = null;
        }
        isSearching = false;
    }

    private IEnumerator SearchWithDelay(string query)
    {
        yield return new WaitForSeconds(searchDelay);

        // Verify query hasn't changed during delay
        if (query.Trim() != searchInputField.text.Trim())
            yield break;

        // Avoid duplicate searches
        if (query == lastSearchQuery)
            yield break;

        yield return StartCoroutine(SearchPlaces(query));
    }

    private IEnumerator SearchPlaces(string query)
    {
        isSearching = true;
        lastSearchQuery = query;

        Debug.Log($"[AutoSuggestionManager] Searching for: '{query}'");

        // Call Mapbox API
        yield return StartCoroutine(mapboxApi.QueryLocal(query, false)); // Set verbose to false for production

        isSearching = false;

        // Process results
        if (mapboxApi.QueryLocalResult?.features != null && mapboxApi.QueryLocalResult.features.Count > 0)
        {
            var features = mapboxApi.QueryLocalResult.features;
            
            // Cache the result
            if (enableCaching)
            {
                CacheResult(query, features);
            }
            
            DisplaySuggestions(features);
        }
        else if (!string.IsNullOrEmpty(mapboxApi.ErrorMessage))
        {
            Debug.LogWarning($"[AutoSuggestionManager] Search error: {mapboxApi.ErrorMessage}");
            ShowNoResultsMessage();
        }
        else
        {
            ShowNoResultsMessage();
        }
    }

    #endregion

    #region Caching

    private bool TryGetCachedResult(string query, out List<GeocodingFeature> features)
    {
        features = null;
        
        if (!enableCaching || !searchCache.ContainsKey(query))
            return false;

        var cachedResult = searchCache[query];
        if (cachedResult.IsValid())
        {
            features = cachedResult.features;
            Debug.Log($"[AutoSuggestionManager] Using cached result for: '{query}'");
            return true;
        }
        else
        {
            // Remove expired cache entry
            searchCache.Remove(query);
            return false;
        }
    }

    private void CacheResult(string query, List<GeocodingFeature> features)
    {
        // Manage cache size
        while (searchCache.Count >= maxCacheSize && cacheOrder.Count > 0)
        {
            string oldestKey = cacheOrder.Dequeue();
            searchCache.Remove(oldestKey);
        }

        // Cache new result
        searchCache[query] = new CachedResult(features);
        cacheOrder.Enqueue(query);
    }

    private void ClearCache()
    {
        searchCache.Clear();
        cacheOrder.Clear();
    }

    #endregion

    #region UI Management

    private void DisplaySuggestions(List<GeocodingFeature> features)
    {
        if (features == null || features.Count == 0)
        {
            ShowNoResultsMessage();
            return;
        }

        // Clear existing suggestions
        ClearActiveSuggestions();

        // Show suggestion panel
        suggestionPanel.SetActive(true);

        // Create suggestion items
        int count = Mathf.Min(features.Count, maxSuggestions);
        for (int i = 0; i < count; i++)
        {
            CreateSuggestionItem(features[i], i);
        }

        // Reset scroll position
        if (suggestionScrollRect != null)
        {
            suggestionScrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void CreateSuggestionItem(GeocodingFeature feature, int index)
    {
        GameObject suggestionItem = GetPooledSuggestionItem();
        if (suggestionItem == null) return;

        suggestionItem.SetActive(true);

        // Configure the suggestion item
        ConfigureSuggestionItem(suggestionItem, feature, index);

        // Add to active suggestions
        activeSuggestionItems.Add(suggestionItem);
    }

    private GameObject GetPooledSuggestionItem()
    {
        if (suggestionItemPool.Count > 0)
        {
            return suggestionItemPool.Dequeue();
        }
        
        // Create new item if pool is empty
        return Instantiate(suggestionItemPrefab, suggestionPanel.transform);
    }

    private void ConfigureSuggestionItem(GameObject suggestionItem, GeocodingFeature feature, int index)
    {
        // Get components
        Button button = suggestionItem.GetComponent<Button>();
        TMP_Text textComponent = suggestionItem.GetComponentInChildren<TMP_Text>();

        if (textComponent != null)
        {
            // Use the most appropriate display text
            string displayText = GetDisplayText(feature);
            textComponent.text = displayText;
        }

        // Configure button
        if (button != null)
        {
            // Clear previous listeners
            button.onClick.RemoveAllListeners();
            
            // Add new listener
            button.onClick.AddListener(() => OnSuggestionSelected(feature));
            
            // Add keyboard navigation support
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            button.navigation = navigation;
        }

        // Add hover effects (optional)
        AddHoverEffects(suggestionItem);
    }

    private string GetDisplayText(GeocodingFeature feature)
    {
        if (!string.IsNullOrEmpty(feature.place_name))
            return feature.place_name;
        
        if (!string.IsNullOrEmpty(feature.text))
            return feature.text;
            
        return "Unknown Location";
    }

    private void AddHoverEffects(GameObject suggestionItem)
    {
        // Optional: Add visual feedback on hover
        var button = suggestionItem.GetComponent<Button>();
        if (button != null)
        {
            var colors = button.colors;
            colors.highlightedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
        }
    }

    private void ShowNoResultsMessage()
    {
        ClearActiveSuggestions();
        
        // Optionally show a "No results found" message
        GameObject noResultsItem = GetPooledSuggestionItem();
        if (noResultsItem != null)
        {
            noResultsItem.SetActive(true);
            
            var textComponent = noResultsItem.GetComponentInChildren<TMP_Text>();
            if (textComponent != null)
            {
                textComponent.text = "No locations found";
                textComponent.color = Color.gray;
            }
            
            var button = noResultsItem.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = false;
            }
            
            activeSuggestionItems.Add(noResultsItem);
            suggestionPanel.SetActive(true);
        }
    }

    public void HideSuggestions()
    {
        if (suggestionPanel != null)
        {
            suggestionPanel.SetActive(false);
        }
        ClearActiveSuggestions();
    }

    private void ClearActiveSuggestions()
    {
        // Return items to pool instead of destroying them
        foreach (GameObject item in activeSuggestionItems)
        {
            if (item != null)
            {
                item.SetActive(false);
                
                // Reset item state
                var textComponent = item.GetComponentInChildren<TMP_Text>();
                if (textComponent != null)
                {
                    textComponent.color = Color.black; // Reset color
                }
                
                var button = item.GetComponent<Button>();
                if (button != null)
                {
                    button.interactable = true; // Reset interactability
                    button.onClick.RemoveAllListeners(); // Clear listeners
                }
                
                suggestionItemPool.Enqueue(item);
            }
        }
        activeSuggestionItems.Clear();
    }

    #endregion

    #region Selection Handling

    private void OnSuggestionSelected(GeocodingFeature selectedFeature)
    {
        if (selectedFeature == null) return;

        lastSelectedFeature = selectedFeature;

        // Update input field
        if (searchInputField != null)
        {
            string displayText = GetDisplayText(selectedFeature);
            searchInputField.text = displayText;
        }

        // Hide suggestions
        HideSuggestions();

        // Trigger events
        OnPlaceSelected(selectedFeature);
        OnPlaceSelectedEvent?.Invoke(selectedFeature);

        Debug.Log($"[AutoSuggestionManager] Selected: {selectedFeature.text} at {GetCoordinatesString(selectedFeature)}");
    }

    private void OnPlaceSelected(GeocodingFeature selectedPlace)
    {
        if (selectedPlace?.geometry?.coordinates != null && selectedPlace.geometry.coordinates.Count > 0)
        {
            Location selectedLocation = selectedPlace.geometry.coordinates[0];
            
            Debug.Log($"[AutoSuggestionManager] Selected Location: Lat={selectedLocation.Latitude:F6}, Lon={selectedLocation.Longitude:F6}, Alt={selectedLocation.Altitude:F2}");
            
            // Custom logic for place selection can be added here
        }
        else
        {
            Debug.LogWarning("[AutoSuggestionManager] Selected place does not have valid coordinates");
        }
    }

    private string GetCoordinatesString(GeocodingFeature feature)
    {
        if (feature?.geometry?.coordinates != null && feature.geometry.coordinates.Count > 0)
        {
            var location = feature.geometry.coordinates[0];
            return $"({location.Latitude:F6}, {location.Longitude:F6})";
        }
        return "Unknown coordinates";
    }

    #endregion

    #region Public API

    /// <summary>
    /// Manually trigger a search for the given query
    /// </summary>
    public void SearchManually(string query)
    {
        if (searchInputField != null)
        {
            searchInputField.text = query;
        }
        OnSearchInputChanged(query);
    }

    /// <summary>
    /// Clear the current search and hide suggestions
    /// </summary>
    public void ClearSearch()
    {
        if (searchInputField != null)
        {
            searchInputField.text = "";
        }
        HideSuggestions();
    }

    /// <summary>
    /// Get the last selected feature
    /// </summary>
    public GeocodingFeature GetLastSelectedFeature()
    {
        return lastSelectedFeature;
    }

    /// <summary>
    /// Check if a search is currently in progress
    /// </summary>
    public bool IsSearching()
    {
        return isSearching;
    }

    #endregion

    #region Editor Helpers

    #if UNITY_EDITOR
    [ContextMenu("Test Search")]
    private void TestSearch()
    {
        SearchManually("Test Location");
    }

    [ContextMenu("Clear Cache")]
    private void ClearCacheFromEditor()
    {
        ClearCache();
        Debug.Log("[AutoSuggestionManager] Cache cleared!");
    }
    #endif

    #endregion
}