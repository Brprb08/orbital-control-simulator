using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns hold-repeat button setup and selected styling for manual velocity intent buttons.
/// </summary>
public sealed class ManualVelocityButtonBinder
{
    private readonly ButtonSelectionGroup speedIntentButtonGroup;
    private readonly int circularizeButtonIndex;
    private readonly int raiseApogeeButtonIndex;
    private readonly int lowerPerigeeButtonIndex;
    private readonly ButtonSelectionGroup baseDirectionButtonGroup;
    private readonly int progradeButtonIndex;
    private readonly int retrogradeButtonIndex;

    private readonly Button radialOutHoldButton;
    private readonly Button radialInHoldButton;
    private readonly Button tiltPositiveHoldButton;
    private readonly Button tiltNegativeHoldButton;
    private readonly float holdInitialDelay;
    private readonly float holdRepeatInterval;
    private readonly float holdFastRepeatInterval;
    private readonly float holdAccelerationDelay;

    public ManualVelocityButtonBinder(
        ButtonSelectionGroup speedIntentButtonGroup,
        int circularizeButtonIndex,
        int raiseApogeeButtonIndex,
        int lowerPerigeeButtonIndex,
        ButtonSelectionGroup baseDirectionButtonGroup,
        int progradeButtonIndex,
        int retrogradeButtonIndex,
        Button radialOutHoldButton,
        Button radialInHoldButton,
        Button tiltPositiveHoldButton,
        Button tiltNegativeHoldButton,
        float holdInitialDelay,
        float holdRepeatInterval,
        float holdFastRepeatInterval,
        float holdAccelerationDelay)
    {
        this.speedIntentButtonGroup = speedIntentButtonGroup;
        this.circularizeButtonIndex = circularizeButtonIndex;
        this.raiseApogeeButtonIndex = raiseApogeeButtonIndex;
        this.lowerPerigeeButtonIndex = lowerPerigeeButtonIndex;
        this.baseDirectionButtonGroup = baseDirectionButtonGroup;
        this.progradeButtonIndex = progradeButtonIndex;
        this.retrogradeButtonIndex = retrogradeButtonIndex;
        this.radialOutHoldButton = radialOutHoldButton;
        this.radialInHoldButton = radialInHoldButton;
        this.tiltPositiveHoldButton = tiltPositiveHoldButton;
        this.tiltNegativeHoldButton = tiltNegativeHoldButton;
        this.holdInitialDelay = holdInitialDelay;
        this.holdRepeatInterval = holdRepeatInterval;
        this.holdFastRepeatInterval = holdFastRepeatInterval;
        this.holdAccelerationDelay = holdAccelerationDelay;
    }

    public void ConfigureHoldButtons(
        System.Action radialOut,
        System.Action radialIn,
        System.Action tiltPositive,
        System.Action tiltNegative)
    {
        ConfigureHoldButton(radialOutHoldButton, radialOut);
        ConfigureHoldButton(radialInHoldButton, radialIn);
        ConfigureHoldButton(tiltPositiveHoldButton, tiltPositive);
        ConfigureHoldButton(tiltNegativeHoldButton, tiltNegative);
    }

    public void ClearHoldButtons()
    {
        ClearHoldButton(radialOutHoldButton);
        ClearHoldButton(radialInHoldButton);
        ClearHoldButton(tiltPositiveHoldButton);
        ClearHoldButton(tiltNegativeHoldButton);
    }

    public void RefreshSpeedIntent(ManualOrbitSpeedIntentSelection selection)
    {
        if (speedIntentButtonGroup == null)
            return;

        switch (selection)
        {
            case ManualOrbitSpeedIntentSelection.Circularize:
                speedIntentButtonGroup.Select(circularizeButtonIndex);
                break;
            case ManualOrbitSpeedIntentSelection.RaiseApogee:
                speedIntentButtonGroup.Select(raiseApogeeButtonIndex);
                break;
            case ManualOrbitSpeedIntentSelection.LowerPerigee:
                speedIntentButtonGroup.Select(lowerPerigeeButtonIndex);
                break;
            default:
                speedIntentButtonGroup.Clear();
                break;
        }
    }

    public void RefreshBaseDirection(bool hasPendingPlacement, ManualOrbitBaseDirection direction)
    {
        if (baseDirectionButtonGroup == null)
            return;

        if (!hasPendingPlacement)
        {
            baseDirectionButtonGroup.Clear();
            return;
        }

        if (direction == ManualOrbitBaseDirection.Retrograde)
            baseDirectionButtonGroup.Select(retrogradeButtonIndex);
        else
            baseDirectionButtonGroup.Select(progradeButtonIndex);
    }

    private void ConfigureHoldButton(Button button, System.Action action)
    {
        if (button == null || action == null)
            return;

        button.onClick = new Button.ButtonClickedEvent();

        HoldRepeatButton holdButton = button.GetComponent<HoldRepeatButton>();
        if (holdButton == null)
            holdButton = button.gameObject.AddComponent<HoldRepeatButton>();

        holdButton.SetTiming(
            holdInitialDelay,
            holdRepeatInterval,
            holdFastRepeatInterval,
            holdAccelerationDelay
        );
        holdButton.Configure(action);
    }

    private static void ClearHoldButton(Button button)
    {
        if (button == null)
            return;

        HoldRepeatButton holdButton = button.GetComponent<HoldRepeatButton>();
        holdButton?.Clear();
    }
}
