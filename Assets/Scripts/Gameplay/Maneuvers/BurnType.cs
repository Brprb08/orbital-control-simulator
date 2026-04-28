using System;

public enum BurnType
{
    Prograde = 0,
    Retrograde = 1,
    RadialIn = 2,
    RadialOut = 3,
    Normal = 4,
    AntiNormal = 5
}

public static class BurnTypeExtensions
{
    // Display names for dropdown / UI
    private static readonly string[] _labels =
    {
        "Prograde",
        "Retrograde",
        "Radial In",
        "Radial Out",
        "Normal",
        "Anti-Normal"
    };

    public static string ToDisplayName(this BurnType type)
    {
        int i = (int)type;
        if (i < 0 || i >= _labels.Length)
            return "Prograde";
        return _labels[i];
    }

    public static BurnType FromDisplayName(string label)
    {
        if (string.IsNullOrEmpty(label))
            return BurnType.Prograde;

        for (int i = 0; i < _labels.Length; i++)
        {
            if (string.Equals(_labels[i], label, StringComparison.OrdinalIgnoreCase))
                return (BurnType)i;
        }

        return BurnType.Prograde;
    }

    public static BurnType FromDropdownIndex(int index)
    {
        if (index < 0 || index >= _labels.Length)
            return BurnType.Prograde;
        return (BurnType)index;
    }

    public static int ToDropdownIndex(this BurnType type)
    {
        int i = (int)type;
        if (i < 0 || i >= _labels.Length)
            return 0;
        return i;
    }
}
