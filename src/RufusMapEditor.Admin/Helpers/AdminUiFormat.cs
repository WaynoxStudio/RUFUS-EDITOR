using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RufusMapEditor.Admin.Helpers;

public sealed class BoolPermitidoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Sí" : "No";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
