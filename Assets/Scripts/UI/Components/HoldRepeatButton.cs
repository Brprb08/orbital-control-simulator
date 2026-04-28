using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class HoldRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private float initialDelay = 0.3f;
    [SerializeField] private float repeatInterval = 0.1f;
    [SerializeField] private float acceleratedRepeatInterval = 0.04f;
    [SerializeField] private float accelerationDelay = 0.8f;

    private Button button;
    private Action clickAction;
    private bool pointerHeld;
    private bool repeatedWhileHeld;
    private float pointerDownTime;
    private float nextRepeatTime;

    public void Configure(Action action)
    {
        clickAction = action;
    }

    public void SetTiming(float delay, float interval, float fastInterval, float accelDelay)
    {
        initialDelay = Mathf.Max(0.01f, delay);
        repeatInterval = Mathf.Max(0.01f, interval);
        acceleratedRepeatInterval = Mathf.Max(0.01f, fastInterval);
        accelerationDelay = Mathf.Max(0.01f, accelDelay);
    }

    public void Clear()
    {
        clickAction = null;
        pointerHeld = false;
        repeatedWhileHeld = false;
    }

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void Update()
    {
        if (!pointerHeld || clickAction == null || !IsInteractable())
            return;

        float now = Time.unscaledTime;
        if (now < nextRepeatTime)
            return;

        repeatedWhileHeld = true;
        clickAction.Invoke();

        float heldDuration = now - pointerDownTime;
        float interval = heldDuration >= accelerationDelay
            ? acceleratedRepeatInterval
            : repeatInterval;
        nextRepeatTime = now + Mathf.Max(0.01f, interval);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!IsInteractable())
            return;

        pointerHeld = true;
        repeatedWhileHeld = false;
        pointerDownTime = Time.unscaledTime;
        nextRepeatTime = pointerDownTime + Mathf.Max(0.01f, initialDelay);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pointerHeld = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerHeld = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (repeatedWhileHeld || clickAction == null || !IsInteractable())
            return;

        clickAction.Invoke();
    }

    private bool IsInteractable()
    {
        return button == null || button.IsInteractable();
    }
}
