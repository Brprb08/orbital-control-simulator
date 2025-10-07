using System;
using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Composition-based observer for TMP_Dropdown popup lifecycle.
/// Fires events when the runtime "Dropdown List" is shown/hidden,
/// without subclassing TMP internals.
/// </summary>
[RequireComponent(typeof(TMP_Dropdown))]
public class ObservableTMPDropdown : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    /// <summary>Raised when the popup list becomes visible. Param = the list RectTransform.</summary>
    public event Action<RectTransform> OnDropdownShown;
    /// <summary>Raised when the popup list is hidden/destroyed.</summary>
    public event Action OnDropdownHidden;

    [Header("Options")]
    [Tooltip("If true, as a last resort do a global GameObject.Find(\"Dropdown List\"). Keep off in big scenes.")]
    [SerializeField] private bool allowGlobalFindFallback = false;

    private TMP_Dropdown _dropdown;
    private Coroutine _probeCo;
    private RectTransform _activeList;
    private Transform _rootCanvas; // nearest root Canvas transform for scoped finds

    // Cache the private field info once per domain instead of reflecting every open.
    private static readonly FieldInfo s_mDropdownField =
        typeof(TMP_Dropdown).GetField("m_Dropdown", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>True while the popup is open.</summary>
    public bool IsOpen => _activeList != null;

    private void Awake()
    {
        _dropdown = GetComponent<TMP_Dropdown>();

        // Find the nearest root canvas once for cheap, scoped searching.
        var canvas = GetComponentInParent<Canvas>();
        _rootCanvas = (canvas != null ? canvas.rootCanvas.transform : transform.root);
    }

    private void OnDisable()
    {
        if (_probeCo != null)
        {
            StopCoroutine(_probeCo);
            _probeCo = null;
        }
        _activeList = null; // ensure IsOpen false if we got disabled mid-popup
    }

    public void OnPointerClick(PointerEventData _) => StartProbe();
    public void OnSubmit(BaseEventData _) => StartProbe(); // keyboard/gamepad

    private void StartProbe()
    {
        if (!isActiveAndEnabled || _probeCo != null) return;
        _probeCo = StartCoroutine(ProbeForPopup());
    }

    private IEnumerator ProbeForPopup()
    {
        // Let TMP spawn its popup this frame.
        yield return null;

        RectTransform rt = TryGetPopupViaReflection()
                        ?? TryGetPopupScoped()
                        ?? (allowGlobalFindFallback ? TryGetPopupGlobal() : null);

        if (rt != null)
        {
            _activeList = rt;
            OnDropdownShown?.Invoke(_activeList);

            // Idle until hidden or destroyed.
            while (_activeList != null && _activeList.gameObject.activeInHierarchy)
                yield return null;

            _activeList = null;
            OnDropdownHidden?.Invoke();
        }

        _probeCo = null;
    }

    private RectTransform TryGetPopupViaReflection()
    {
        if (s_mDropdownField == null || _dropdown == null) return null;

        // Access TMP_Dropdown.m_Dropdown (GameObject) if present.
        var dropdownGO = s_mDropdownField.GetValue(_dropdown) as GameObject;
        if (dropdownGO != null && dropdownGO.activeInHierarchy)
            return dropdownGO.GetComponent<RectTransform>();

        return null;
    }

    private RectTransform TryGetPopupScoped()
    {
        if (_rootCanvas == null) return null;

        // TMP names the popup object exactly "Dropdown List".
        var found = _rootCanvas.Find("Dropdown List");
        return (found != null && found.gameObject.activeInHierarchy)
            ? found.GetComponent<RectTransform>()
            : null;
    }

    private static RectTransform TryGetPopupGlobal()
    {
        var go = GameObject.Find("Dropdown List");
        return (go != null && go.activeInHierarchy)
            ? go.GetComponent<RectTransform>()
            : null;
    }
}
