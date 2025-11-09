using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems; // NEW: for clearing selection
using UnityEngine.UI;

public class AttitudeUIBinder : MonoBehaviour
{
    public ICameraTracker cameraTracker;
    public Button btnPrograde, btnRetrograde, btnNadir, btnZenith, btnNormal, btnAntiNormal;
    public Button btnHold;
    public TextMeshProUGUI currentAttitudeLockText;
    private TextMeshProUGUI btnText;
    public Toggle tSnap;
    public Slider slewRate;

    private Dictionary<AttitudeController.PointingMode, Button> modeToButton;

    // ui state
    private bool attitudeLocked = false;
    private AttitudeController.PointingMode lastAutoMode = AttitudeController.PointingMode.Velocity;

    AttitudeController CurrentAtt =>
        cameraTracker?.CurrentBody
            ? cameraTracker.CurrentBody.GetComponent<AttitudeController>()
            : null;

    // NEW: track changes to body and mode so we can auto-refresh
    private NBody _lastBody;
    private AttitudeController.PointingMode _lastModeMirror;
    private bool _haveMirror = false;

    public void Initialize(SimContext ctx) { this.cameraTracker = ctx.CameraTracker; }

    void Awake()
    {
        modeToButton = new Dictionary<AttitudeController.PointingMode, Button>
        {
            { AttitudeController.PointingMode.Velocity,    btnPrograde   },
            { AttitudeController.PointingMode.Retrograde,  btnRetrograde },
            { AttitudeController.PointingMode.Nadir,       btnNadir      },
            { AttitudeController.PointingMode.Zenith,      btnZenith     },
            { AttitudeController.PointingMode.Normal,      btnNormal     },
            { AttitudeController.PointingMode.AntiNormal,  btnAntiNormal }
        };
    }

    void Start()
    {
        if (btnHold) btnText = btnHold.GetComponentInChildren<TextMeshProUGUI>();
        SetHoldUI(false); // start in auto
        ForceFullRefresh(); // NEW: makes sure first frame is correct
    }

    void OnEnable()
    {
        btnPrograde.onClick.AddListener(() => SetMode(AttitudeController.PointingMode.Velocity));
        btnRetrograde.onClick.AddListener(() => SetMode(AttitudeController.PointingMode.Retrograde));
        btnNadir.onClick.AddListener(() => SetMode(AttitudeController.PointingMode.Nadir));
        btnZenith.onClick.AddListener(() => SetMode(AttitudeController.PointingMode.Zenith));
        btnNormal.onClick.AddListener(() => SetMode(AttitudeController.PointingMode.Normal));
        btnAntiNormal.onClick.AddListener(() => SetMode(AttitudeController.PointingMode.AntiNormal));

        if (btnHold) btnHold.onClick.AddListener(HoldHere);

        if (tSnap) tSnap.onValueChanged.AddListener(SetSnap);
        if (slewRate) slewRate.onValueChanged.AddListener(SetSlew);

        ForceFullRefresh(); // NEW
    }

    void OnDisable()
    {
        btnPrograde.onClick.RemoveAllListeners();
        btnRetrograde.onClick.RemoveAllListeners();
        btnNadir.onClick.RemoveAllListeners();
        btnZenith.onClick.RemoveAllListeners();
        btnNormal.onClick.RemoveAllListeners();
        btnAntiNormal.onClick.RemoveAllListeners();
        if (btnHold) btnHold.onClick.RemoveAllListeners();

        if (tSnap) tSnap.onValueChanged.RemoveAllListeners();
        if (slewRate) slewRate.onValueChanged.RemoveAllListeners();
    }

    // NEW: watch for body and external mode changes
    void Update()
    {
        var currentBody = cameraTracker?.CurrentBody;
        if (currentBody != _lastBody)
        {
            _lastBody = currentBody;
            ForceFullRefresh();
            return;
        }

        var att = CurrentAtt;
        if (!att) return;

        if (!_haveMirror || att.mode != _lastModeMirror)
        {
            // Someone changed the mode elsewhere → reflect it
            _lastModeMirror = att.mode;
            _haveMirror = true;
            RefreshUIFrom(att);
            UpdateModeButtons(att.mode);
        }
    }

    // ----- MODE CHANGES -----

    void SetMode(AttitudeController.PointingMode m)
    {
        var att = CurrentAtt;
        if (!att) return;

        // if we were holding and user selects an auto mode, exit hold
        if (att.mode == AttitudeController.PointingMode.HoldCurrent &&
            m != AttitudeController.PointingMode.HoldCurrent)
        {
            SetHoldUI(false);
        }

        if (m != AttitudeController.PointingMode.HoldCurrent)
            lastAutoMode = m;

        att.SetMode(m);

        // mirror state immediately so Update() doesn’t fight us this frame
        _lastModeMirror = att.mode;
        _haveMirror = true;

        RefreshUIFrom(att);
        UpdateModeButtons(att.mode);
    }

    void HoldHere()
    {
        var att = CurrentAtt;
        if (!att) return;

        bool goingToHold = att.mode != AttitudeController.PointingMode.HoldCurrent;

        if (goingToHold)
        {
            // entering Hold: remember current auto mode, then freeze
            if (att.mode != AttitudeController.PointingMode.HoldCurrent)
                lastAutoMode = att.mode;

            att.FreezeCurrentAttitude(); // sets HoldCurrent
            SetHoldUI(true);
        }
        else
        {
            // leaving Hold: restore last auto mode
            att.SetMode(lastAutoMode);
            SetHoldUI(false);
        }

        _lastModeMirror = att.mode; // NEW: keep mirror in sync
        _haveMirror = true;

        RefreshUIFrom(att);
        UpdateModeButtons(att.mode);
    }

    // ----- UI SYNC -----

    void SetHoldUI(bool isLocked)
    {
        attitudeLocked = isLocked;
        if (btnText) btnText.text = isLocked ? "Auto Track" : "Lock Attitude";
        if (currentAttitudeLockText)
            currentAttitudeLockText.text = isLocked ? "Attitude is Locked" : "Attitude is Auto Tracking";
    }

    void UpdateModeButtons(AttitudeController.PointingMode active)
    {
        // reset all first so old craft's disabled button doesn't linger
        foreach (var kv in modeToButton)
        {
            var b = kv.Value;
            if (b) b.interactable = true;
        }

        bool holding = active == AttitudeController.PointingMode.HoldCurrent;

        foreach (var kv in modeToButton)
        {
            var b = kv.Value;
            if (!b) continue;

            if (holding)
            {
                b.interactable = true;   // in Hold, none is "selected"
            }
            else
            {
                b.interactable = kv.Key != active; // disable the active one
            }
        }

        // Lock/Auto button never changes color; always interactable
        if (btnHold) btnHold.interactable = true;

        // NEW: clear Unity's current selection to avoid ghost highlight from previous satellite
        if (EventSystem.current && EventSystem.current.currentSelectedGameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }

    void RefreshUIFromCurrent()
    {
        var att = CurrentAtt;
        if (!att) return;

        bool lockedNow = att.mode == AttitudeController.PointingMode.HoldCurrent;
        SetHoldUI(lockedNow);

        if (!lockedNow)
            lastAutoMode = att.mode;

        _lastModeMirror = att.mode; // NEW
        _haveMirror = true;

        RefreshUIFrom(att);
        UpdateModeButtons(att.mode);
    }

    void RefreshUIFrom(AttitudeController att)
    {
        if (tSnap) tSnap.SetIsOnWithoutNotify(att.snapAttitude);
        if (slewRate) slewRate.SetValueWithoutNotify(att.maxSlewRateDegPerSec);
    }

    // ----- other controls -----

    void SetSnap(bool snap) { var att = CurrentAtt; if (att) att.snapAttitude = snap; }
    void SetSlew(float degs) { var att = CurrentAtt; if (att) att.maxSlewRateDegPerSec = degs; }

    // NEW: force a full refresh (used on Start/OnEnable/body switch)
    void ForceFullRefresh()
    {
        // clear old selection visuals
        foreach (var kv in modeToButton)
            if (kv.Value) kv.Value.interactable = true;

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);

        RefreshUIFromCurrent();
    }
}
