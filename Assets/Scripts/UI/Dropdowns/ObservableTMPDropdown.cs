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
    private Transform _rootCanvas; // nearest root Canvas for scoped lookup

    // Cached reflection handle to TMP_Dropdown.m_Dropdown (private)
    private static readonly FieldInfo s_mDropdownField =
        typeof(TMP_Dropdown).GetField("m_Dropdown", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>True while the popup is currently open.</summary>
    public bool IsOpen => _activeList != null;

    /// <summary>
    /// Caches local references and locates the nearest root canvas for scoped searches.
    /// </summary>
    private void Awake()
    {
        _dropdown = GetComponent<TMP_Dropdown>();

        var canvas = GetComponentInParent<Canvas>();
        _rootCanvas = (canvas != null ? canvas.rootCanvas.transform : transform.root);
    }

    /// <summary>
    /// Stops any active probe and clears state if disabled mid-popup.
    /// </summary>
    private void OnDisable()
    {
        if (_probeCo != null)
        {
            StopCoroutine(_probeCo);
            _probeCo = null;
        }
        _activeList = null;
    }

    /// <summary>
    /// Starts a popup probe on pointer click.
    /// </summary>
    public void OnPointerClick(PointerEventData _) => StartProbe();

    /// <summary>
    /// Starts a popup probe on keyboard/gamepad submit.
    /// </summary>
    public void OnSubmit(BaseEventData _) => StartProbe();

    /// <summary>
    /// Begins the coroutine that detects the spawned popup list.
    /// </summary>
    private void StartProbe()
    {
        if (!isActiveAndEnabled || _probeCo != null) return;
        _probeCo = StartCoroutine(ProbeForPopup());
    }

    /// <summary>
    /// Waits a frame for TMP to spawn the list, then resolves the popup via:
    /// reflection → scoped find under root canvas → optional global find.
    /// Emits open/close events accordingly.
    /// </summary>
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

    /// <summary>
    /// Reads TMP_Dropdown.m_Dropdown (GameObject) via reflection and returns its RectTransform if active.
    /// </summary>
    private RectTransform TryGetPopupViaReflection()
    {
        if (s_mDropdownField == null || _dropdown == null) return null;

        var dropdownGO = s_mDropdownField.GetValue(_dropdown) as GameObject;
        if (dropdownGO != null && dropdownGO.activeInHierarchy)
            return dropdownGO.GetComponent<RectTransform>();

        return null;
    }

    /// <summary>
    /// Searches for a child named “Dropdown List” under the nearest root canvas.
    /// </summary>
    private RectTransform TryGetPopupScoped()
    {
        if (_rootCanvas == null) return null;

        var found = _rootCanvas.Find("Dropdown List");
        return (found != null && found.gameObject.activeInHierarchy)
            ? found.GetComponent<RectTransform>()
            : null;
    }

    /// <summary>
    /// Global scene search for “Dropdown List” (expensive; optional).
    /// </summary>
    private static RectTransform TryGetPopupGlobal()
    {
        var go = GameObject.Find("Dropdown List");
        return (go != null && go.activeInHierarchy)
            ? go.GetComponent<RectTransform>()
            : null;
    }
}
