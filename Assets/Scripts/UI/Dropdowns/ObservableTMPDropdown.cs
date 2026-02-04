using System;
using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Observes a TMP_Dropdown’s popup lifecycle without subclassing TMP internals.
/// Emits events when the runtime “Dropdown List” opens and closes by probing
/// the created list object via reflection or scoped hierarchy search.
/// </summary>
[RequireComponent(typeof(TMP_Dropdown))]
public class ObservableTMPDropdown : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    /// <summary>Invoked when the popup list appears. Argument is the list RectTransform.</summary>
    public event Action<RectTransform> OnDropdownShown;

    /// <summary>Invoked when the popup list is hidden or destroyed.</summary>
    public event Action OnDropdownHidden;

    [Header("Options")]
    [Tooltip("As a last resort, search globally for \"Dropdown List\". Avoid enabling in large scenes.")]
    [SerializeField] private bool allowGlobalFindFallback = false;

    private TMP_Dropdown _dropdown;
    private Coroutine _probeCo;
    private RectTransform _activeList;
    private Transform _rootCanvas;

    // Cached reflection handle to TMP_Dropdown.m_Dropdown (private)
    private static readonly FieldInfo s_mDropdownField =
        typeof(TMP_Dropdown).GetField("m_Dropdown", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>True while the popup is currently open.</summary>
    public bool IsOpen => _activeList != null;

    private void Awake()
    {
        _dropdown = GetComponent<TMP_Dropdown>();

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

        _activeList = null;
    }

    public void OnPointerClick(PointerEventData _) => StartProbe();

    public void OnSubmit(BaseEventData _) => StartProbe();

    private void StartProbe()
    {
        if (!isActiveAndEnabled || _probeCo != null) return;
        _probeCo = StartCoroutine(ProbeForPopup());
    }

    private IEnumerator ProbeForPopup()
    {
        yield return null;

        RectTransform rt = TryGetPopupViaReflection()
                        ?? TryGetPopupScoped()
                        ?? (allowGlobalFindFallback ? TryGetPopupGlobal() : null);

        if (rt != null)
        {
            _activeList = rt;
            OnDropdownShown?.Invoke(_activeList);

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

        var dropdownGO = s_mDropdownField.GetValue(_dropdown) as GameObject;
        if (dropdownGO != null && dropdownGO.activeInHierarchy)
            return dropdownGO.GetComponent<RectTransform>();

        return null;
    }

    private RectTransform TryGetPopupScoped()
    {
        if (_rootCanvas == null) return null;

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
