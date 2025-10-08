using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Enables Tab/Shift+Tab navigation between TMP_InputFields,
/// and triggers a "Place" button when Enter is pressed on the last field.
/// </summary>
public class TabNavigation : MonoBehaviour
{
    [Tooltip("Input fields in desired tab order")]
    public TMP_InputField[] inputFields;

    [Tooltip("Optional button to click when Enter is pressed on the last field")]
    public Button placeButton;

    void Update()
    {
        if (inputFields == null || inputFields.Length == 0)
            return;

        // Get currently focused field
        GameObject current = EventSystem.current.currentSelectedGameObject;
        if (current == null)
            return;

        // TAB navigation
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            for (int i = 0; i < inputFields.Length; i++)
            {
                if (current == inputFields[i].gameObject)
                {
                    int next = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        ? (i - 1 + inputFields.Length) % inputFields.Length
                        : (i + 1) % inputFields.Length;

                    inputFields[next].Select();
                    inputFields[next].ActivateInputField();
                    return;
                }
            }
        }

        // ENTER on last field triggers button
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            for (int i = 0; i < inputFields.Length; i++)
            {
                if (current == inputFields[i].gameObject)
                {
                    if (i == inputFields.Length - 1 && placeButton != null)
                    {
                        placeButton.onClick.Invoke();
                        EventSystem.current.SetSelectedGameObject(null);
                    }
                    return;
                }
            }
        }
    }
}
