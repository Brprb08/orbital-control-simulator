using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TransparentButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image backgroundImage; // Transparent background
    public Image borderImage; // Always-visible border
    public Color hoverColor = new Color(1f, 1f, 1f, 0.3f); // Hover effect
    private Color originalColor;

    void Start()
    {
        if (backgroundImage == null)
            Debug.LogError("BackgroundImage is not assigned!", this);

        if (borderImage == null)
            Debug.LogError("BorderImage is not assigned!", this);

        // Ensure border is always visible
        if (borderImage != null)
            borderImage.enabled = true;

        // Make button background invisible
        if (backgroundImage != null)
        {
            originalColor = backgroundImage.color;
            backgroundImage.color = new Color(originalColor.r, originalColor.g, originalColor.b, 0.01f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (backgroundImage != null)
            backgroundImage.color = hoverColor; // Light up on hover
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (backgroundImage != null)
            backgroundImage.color = new Color(originalColor.r, originalColor.g, originalColor.b, 0.01f); // Return to transparent
    }
}