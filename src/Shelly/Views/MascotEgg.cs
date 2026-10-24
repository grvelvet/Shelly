using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shelly.Core;
using Shelly.Dialogs;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>1.1.2: a small secret. A triple click on the About icon in Settings shows the Shelly mascot with a random
/// caption. The image is loaded only here, when the window opens.</summary>
public static class MascotEgg
{
    static int lastCaption = -1;

    /// <summary>Opens the window on the third click within 600 ms on <paramref name="target"/>.</summary>
    public static void Attach(FrameworkElement target)
    {
        var counter = new TripleClick();
        target.MouseLeftButtonDown += (_, e) =>
        {
            if (!counter.Click(DateTime.UtcNow)) return;
            e.Handled = true;
            Show();
        };
    }

    public static void Show()
    {
        lastCaption = Mascot.NextCaption(Random.Shared, lastCaption);
        var d = new DialogWindow(Loc.T(Mascot.TitleKey), 348);
        d.HideFooter();
        var img = new Image
        {
            Width = 300, Height = 400, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
            Clip = new RectangleGeometry(new Rect(0, 0, 300, 400), 12, 12),
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/" + Mascot.ResourcePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException) { AppLog.Write("Mascot: " + ex.Message); }
        d.Body.Children.Add(img);
        var cap = DialogWindow.Text(Loc.T(Mascot.CaptionKeys[lastCaption]), null, 14);
        cap.TextAlignment = TextAlignment.Center;
        cap.Margin = new Thickness(0, 16, 0, 0);
        d.Body.Children.Add(cap);
        var coffeeH = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Loc.T("eggCoffee"))) { TextDecorations = null };
        coffeeH.Click += (_, _) => ShellOps.Open(Links.Donate);
        var coffee = new TextBlock(coffeeH) { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), ToolTip = Links.DonateShort };
        d.Body.Children.Add(coffee);
        // A click on the picture or the caption closes it too (Esc and the button already do). Only a click that also
        // started here: the release of the third click that opened the window must not close it.
        bool pressed = false;
        d.Body.MouseLeftButtonDown += (_, _) => pressed = true;
        d.Body.MouseLeftButtonUp += (_, _) => { if (pressed && !coffee.IsMouseOver) d.Close(); };
        d.Run();
    }
}
