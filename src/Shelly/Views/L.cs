using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Shelly.Core;

namespace Shelly.Views;

/// <summary>Localized strings for XAML: {Binding [key], Source={x:Static v:L.I}}; updates when the language changes.</summary>
public sealed class L : INotifyPropertyChanged
{
    public static L I { get; } = new();
    L() { Loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); }
    public string this[string key] => Loc.T(key);
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class InvBoolVis : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>true → Visible, false → Hidden: the element keeps its slot (aligned status marks in list rows).</summary>
public sealed class BoolHidden : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Hidden;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
