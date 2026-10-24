using System.Windows;
using System.Windows.Controls;
using Shelly.Core;

namespace Shelly.Views;

/// <summary>Fluent empty state, centered in its area: a 48 px glyph in secondary color, a short title, one line of
/// explanation and up to two buttons (the first is the accent one). Texts are string keys and follow the language.</summary>
public sealed class EmptyState : StackPanel
{
    readonly TextBlock glyph = new() { FontSize = 48, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock text = new() { FontSize = 14, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
    readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    readonly Button primary = new() { MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button secondary = new() { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
    string titleKey = "", textKey = "", primaryKey = "", secondaryKey = "";
    object?[] args = Array.Empty<object?>();

    public event Action? Primary;
    public event Action? Secondary;

    public EmptyState()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(24);
        MaxWidth = 420;
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        primary.SetResourceReference(StyleProperty, "AccentButtonStyle");
        primary.Click += (_, _) => Primary?.Invoke();
        secondary.Click += (_, _) => Secondary?.Invoke();
        actions.Children.Add(primary);
        actions.Children.Add(secondary);
        Children.Add(glyph);
        Children.Add(title);
        Children.Add(text);
        Children.Add(actions);
        Loc.LanguageChanged += Texts;
    }

    /// <summary>Glyph, title and text keys (the text may take format arguments), button keys ("" = no button).</summary>
    public void Set(string glyphChar, string titleKey, string textKey, string primaryKey = "", string secondaryKey = "", params object?[] args)
    {
        glyph.Text = glyphChar;
        this.titleKey = titleKey; this.textKey = textKey; this.primaryKey = primaryKey; this.secondaryKey = secondaryKey; this.args = args;
        Texts();
    }

    void Texts()
    {
        title.Text = Loc.T(titleKey);
        text.Text = args.Length > 0 ? Loc.F(textKey, args) : Loc.T(textKey);
        primary.Content = primaryKey.Length > 0 ? Loc.T(primaryKey) : "";
        primary.Visibility = primaryKey.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        secondary.Content = secondaryKey.Length > 0 ? Loc.T(secondaryKey) : "";
        secondary.Visibility = secondaryKey.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        actions.Visibility = primaryKey.Length > 0 || secondaryKey.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Show (with the page entrance motion) or hide.</summary>
    public void Show(bool on)
    {
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        if (Visibility == v) return;
        Visibility = v;
        if (on) Motion.Enter(this, 8, 167);
    }
}
