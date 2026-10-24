namespace Shelly.Core;

/// <summary>1.1.2: the small secret in About. Three clicks within <see cref="Window"/> open it; the caption is picked at
/// random from <see cref="CaptionKeys"/> (never the same one twice in a row).</summary>
public sealed class TripleClick
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(600);
    readonly List<DateTime> clicks = new();

    /// <summary>Records a click; true on the third click within the window (then it starts over).</summary>
    public bool Click(DateTime now)
    {
        clicks.RemoveAll(t => now - t > Window || t > now);
        clicks.Add(now);
        if (clicks.Count < 3) return false;
        clicks.Clear();
        return true;
    }
}

public static class Mascot
{
    /// <summary>The image, embedded as a WPF Resource of the Shelly project (450 x 600, about 42 KB).</summary>
    public const string ResourcePath = "Assets/mascot.jpg";
    public const string TitleKey = "eggTitle";
    public static readonly string[] CaptionKeys = { "eggCaption1", "eggCaption2", "eggCaption3", "eggCaption4", "eggCaption5" };

    /// <summary>A caption index other than <paramref name="last"/> (when there is a choice).</summary>
    public static int NextCaption(Random rnd, int last)
    {
        int n = CaptionKeys.Length;
        if (n < 2) return 0;
        int i = rnd.Next(n - 1);
        return last >= 0 && last < n && i >= last ? i + 1 : i;
    }
}
