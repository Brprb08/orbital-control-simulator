using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
        RefreshUIFromCurrent();
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

        RefreshUIFromCurrent();
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
        // rule:
        //  - if holding: all attitude buttons enabled
        //  - if auto: disable the active attitude button; enable the others
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
    }

    void RefreshUIFromCurrent()
    {
        var att = CurrentAtt;
        if (!att) return;

        bool lockedNow = att.mode == AttitudeController.PointingMode.HoldCurrent;
        SetHoldUI(lockedNow);

        if (!lockedNow && att.mode != AttitudeController.PointingMode.HoldCurrent)
            lastAutoMode = att.mode;

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
}
