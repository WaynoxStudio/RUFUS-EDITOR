using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.Admin.Helpers;

public static class AdminUiFormat
{
    public static string StatusLabel(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "active" => "ACTIVA",
        "created" => "CREADA",
        "suspended" => "SUSPENDIDA",
        "revoked" => "REVOCADA",
        "expired" => "CADUCADA",
        _ => string.IsNullOrWhiteSpace(status) ? "—" : status.ToUpperInvariant(),
    };

    public static Brush StatusBrush(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "active" => Brush("#66BB6A"),
        "created" => Brush("#5B8DEF"),
        "suspended" => Brush("#C9A227"),
        "revoked" => Brush("#C45C5C"),
        "expired" => Brush("#8A8F98"),
        _ => Brush("#8A8F98"),
    };

    public static string FormatExpires(DateTimeOffset? expires) =>
        expires is null ? "—" : expires.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    public static string FormatDate(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    public static string FormatQuota(int usage, int? limit) =>
        limit is null || limit <= 0 ? $"{usage} / Sin límite" : $"{usage} / {limit}";

    public static string ShortDeviceId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "—";
        return id.Length <= 12 ? id : id[..12] + "…";
    }

    public static bool IsExpiringSoon(AdminLicenseListItemDto item, DateTimeOffset utcNow)
    {
        if (item.ExpiresAt is null)
            return false;
        var status = (item.Status ?? "").Trim();
        if (status.Equals("Expired", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Revoked", StringComparison.OrdinalIgnoreCase))
            return false;
        var rem = item.ExpiresAt.Value - utcNow;
        return rem > TimeSpan.Zero && rem <= TimeSpan.FromDays(7);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        b.Freeze();
        return b;
    }
}

public sealed class StatusBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        AdminUiFormat.StatusLabel(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        AdminUiFormat.StatusBrush(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

public sealed class ExpiresLocalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        AdminUiFormat.FormatExpires(value as DateTimeOffset?);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

public sealed class BoolPermitidoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Sí" : "No";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
