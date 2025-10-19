using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ConfirmDialog : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text message;
    public Button yesButton;
    public Button noButton;

    private System.Action onConfirm;

    void Awake()
    {
        panel.SetActive(false);
        yesButton.onClick.AddListener(() => { Confirm(); });
        noButton.onClick.AddListener(() => { Cancel(); });
    }

    public void Show(string msg, System.Action confirmAction)
    {
        onConfirm = confirmAction;
        message.text = msg;
        panel.SetActive(true);
    }

    private void Confirm()
    {
        panel.SetActive(false);
        onConfirm?.Invoke();
    }

    private void Cancel()
    {
        panel.SetActive(false);
    }
}
