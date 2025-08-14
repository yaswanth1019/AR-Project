using UnityEngine;
using UnityEngine.EventSystems;

public class ClickOutsideHandler : MonoBehaviour, IPointerClickHandler
{
    [Header("References")]
    public AutoSuggestionManager suggestionManager;
    public GameObject suggestionPanel;
    public GameObject searchInputField;
    
    private bool isPointerOverUI = false;
    
    void Update()
    {
        // Check for mouse click or touch
        if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
        {
            CheckClickOutside();
        }
    }
    
    void CheckClickOutside()
    {
        // Check if pointer is over UI
        isPointerOverUI = EventSystem.current.IsPointerOverGameObject();
        
        if (!isPointerOverUI)
        {
            // Clicked outside UI, hide suggestions
            HideSuggestions();
            return;
        }
        
        // Check if clicked on suggestion panel or input field
        GameObject clickedObject = GetClickedUIObject();
        
        if (clickedObject != null)
        {
            // Check if clicked object is part of the search system
            bool isSearchRelated = IsSearchRelatedObject(clickedObject);
            
            if (!isSearchRelated)
            {
                HideSuggestions();
            }
        }
    }
    
    GameObject GetClickedUIObject()
    {
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        
        // For mouse
        if (Input.mousePresent)
        {
            pointerData.position = Input.mousePosition;
        }
        // For touch
        else if (Input.touchCount > 0)
        {
            pointerData.position = Input.GetTouch(0).position;
        }
        
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        
        if (results.Count > 0)
        {
            return results[0].gameObject;
        }
        
        return null;
    }
    
    bool IsSearchRelatedObject(GameObject obj)
    {
        if (obj == null) return false;
        
        // Check if it's the input field
        if (searchInputField != null && (obj == searchInputField || obj.transform.IsChildOf(searchInputField.transform)))
        {
            return true;
        }
        
        // Check if it's the suggestion panel or its children
        if (suggestionPanel != null && (obj == suggestionPanel || obj.transform.IsChildOf(suggestionPanel.transform)))
        {
            return true;
        }
        
        return false;
    }
    
    void HideSuggestions()
    {
        if (suggestionManager != null)
        {
            // We need to make the HideSuggestions method public in AutoSuggestionManager
            suggestionManager.SendMessage("HideSuggestions", SendMessageOptions.DontRequireReceiver);
        }
        else if (suggestionPanel != null)
        {
            suggestionPanel.SetActive(false);
        }
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        // This can be used if this script is attached to a specific UI element
        // For now, we're handling clicks in Update()
    }
}