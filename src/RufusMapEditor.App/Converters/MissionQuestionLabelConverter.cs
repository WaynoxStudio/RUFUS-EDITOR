using System.Globalization;
using System.Windows.Data;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App.Converters;

public sealed class MissionQuestionLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DialogQuestionDraft q ? MissionDialogRolesUi.FormatQuestionLabel(q) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
