using Xunit;

namespace Shelly.Core.Tests;

/// <summary>1.1.2: the small secret in About (triple click on the About icon shows the mascot).</summary>
public class EasterTests
{
    static string Repo(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(new[] { d.FullName }.Concat(parts).ToArray());
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    static string In(string lang, Func<string> f)
    {
        string old = Loc.Language;
        try { Loc.SetLanguage(lang); return f(); }
        finally { Loc.SetLanguage(old); }
    }

    [Fact]
    public void Captions_exist_in_both_languages()
    {
        Assert.InRange(Mascot.CaptionKeys.Length, 3, 5);
        foreach (var lang in new[] { "en", "ru" })
            foreach (var k in Mascot.CaptionKeys.Append(Mascot.TitleKey))
            {
                string t = In(lang, () => Loc.T(k));
                Assert.NotEqual(k, t);
                Assert.False(string.IsNullOrWhiteSpace(t));
            }
        Assert.Equal("Секретный режим >_", In("ru", () => Loc.T("eggTitle")));
        Assert.Equal("Secret mode >_", In("en", () => Loc.T("eggTitle")));
        // Every caption has its own Russian text (not the English fallback)
        Assert.True(Mascot.CaptionKeys.Count(k => In("ru", () => Loc.T(k)) != In("en", () => Loc.T(k))) == Mascot.CaptionKeys.Length);
    }

    [Fact]
    public void Image_is_embedded_small_and_the_original_is_not()
    {
        string proj = File.ReadAllText(Repo("src", "Shelly", "Shelly.csproj"));
        Assert.Contains(@"<Resource Include=""Assets\mascot.jpg"" />", proj);
        Assert.DoesNotContain(@"assets\mascot.jpg", proj.Replace(@"Assets\mascot.jpg", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Assets/mascot.jpg", Mascot.ResourcePath);
        var img = File.ReadAllBytes(Repo("src", "Shelly", "Assets", "mascot.jpg"));
        Assert.True(img.Length < 64 * 1024, $"{img.Length} bytes");
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, img.Take(2));
        Assert.Equal((450, 600), JpegSize(img));   // crisp at 300 x 400 DIP on 150 % scaling
        Assert.True(File.Exists(Repo("assets", "mascot.jpg")));   // full-size original for the README, not in the exe
    }

    /// <summary>Width and height from the first SOF marker.</summary>
    static (int W, int H) JpegSize(byte[] b)
    {
        int i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            byte m = b[i + 1];
            int len = (b[i + 2] << 8) | b[i + 3];
            if (m is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
            i += 2 + len;
        }
        return (0, 0);
    }

    [Fact]
    public void Three_clicks_within_600_ms_open_it()
    {
        var t0 = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var c = new TripleClick();
        Assert.False(c.Click(t0));
        Assert.False(c.Click(t0.AddMilliseconds(200)));
        Assert.True(c.Click(t0.AddMilliseconds(500)));
        // Starts over after it opened
        Assert.False(c.Click(t0.AddMilliseconds(550)));
        // Too slow: the first click falls out of the window
        var s = new TripleClick();
        Assert.False(s.Click(t0));
        Assert.False(s.Click(t0.AddMilliseconds(400)));
        Assert.False(s.Click(t0.AddMilliseconds(700)));
        Assert.True(s.Click(t0.AddMilliseconds(800)));   // 400, 700, 800 are within 600 ms
        // Two clicks (a double click) never open it
        var d = new TripleClick();
        Assert.False(d.Click(t0));
        Assert.False(d.Click(t0.AddMilliseconds(100)));
        Assert.False(d.Click(t0.AddSeconds(5)));
    }

    [Fact]
    public void Caption_is_random_but_never_the_same_twice_in_a_row()
    {
        var rnd = new Random(7);
        int last = -1;
        var seen = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            int n = Mascot.NextCaption(rnd, last);
            Assert.InRange(n, 0, Mascot.CaptionKeys.Length - 1);
            Assert.NotEqual(last, n);
            seen.Add(n);
            last = n;
        }
        Assert.Equal(Mascot.CaptionKeys.Length, seen.Count);
    }

    [Fact]
    public void Donation_link_is_one_https_constant()
    {
        Assert.Equal("https://boosty.to/koms", Links.Donate);
        Assert.StartsWith("https://", Links.Donate);
        Assert.Equal("boosty.to/koms", Links.DonateShort);
        var u = new Uri(Links.Donate);
        Assert.Equal(("https", "boosty.to", "/koms"), (u.Scheme, u.Host, u.AbsolutePath));
        // The URL lives only in Core: the app code uses the constant
        foreach (var f in new[] { new[] { "src", "Shelly", "Views", "SettingsPage.cs" }, new[] { "src", "Shelly", "Views", "MascotEgg.cs" } })
        {
            string src = File.ReadAllText(Repo(f));
            Assert.Contains("ShellOps.Open(Links.Donate)", src);
            Assert.DoesNotContain("boosty.to", src);
        }
        Assert.Contains("custom: [\"https://boosty.to/koms\"]", File.ReadAllText(Repo(".github", "FUNDING.yml")));
        Assert.Contains("https://boosty.to/koms", File.ReadAllText(Repo("README.md")));
    }

    [Fact]
    public void Donation_texts_exist_in_both_languages()
    {
        Assert.Equal("Поддержать проект ☕", In("ru", () => Loc.T("aboutSupport")));
        Assert.Equal("Support the project ☕", In("en", () => Loc.T("aboutSupport")));
        Assert.Equal("Угостить кофе ☕", In("ru", () => Loc.T("eggCoffee")));
        Assert.Equal("Buy me a coffee ☕", In("en", () => Loc.T("eggCoffee")));
    }

    [Fact]
    public void The_trigger_is_on_the_About_icon()
    {
        string src = File.ReadAllText(Repo("src", "Shelly", "Views", "SettingsPage.cs"));
        Assert.Contains("MascotEgg.Attach(aboutIcon);", src);
        string egg = File.ReadAllText(Repo("src", "Shelly", "Views", "MascotEgg.cs"));
        Assert.Contains("BitmapScalingMode.HighQuality", egg);
        Assert.Contains("new DialogWindow(", egg);   // owner = main window, CenterOwner, not in the taskbar, Esc closes
    }
}
