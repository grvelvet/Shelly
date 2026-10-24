namespace Shelly.Core;

/// <summary>Outside links shown in the app (1.1.2). One place for each URL.</summary>
public static class Links
{
    /// <summary>Donations (Boosty). Shown in Settings > About and in the mascot window; never as a pop-up.</summary>
    public const string Donate = "https://boosty.to/koms";

    /// <summary>The donation URL without the scheme, for tooltips.</summary>
    public static string DonateShort => Donate.Substring(Donate.IndexOf("://", StringComparison.Ordinal) + 3);
}
