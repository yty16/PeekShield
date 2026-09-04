using Avalonia.Media;

namespace PeekShield.Services;

internal static class Palette
{
    private static bool Dark => ThemeService.IsDark;

    public static ISolidColorBrush PageBg => Solid(Dark ? "#16161A" : "#EDF0F5");
    public static ISolidColorBrush CardBg => Solid(Dark ? "#232329" : "#FFFFFF");
    public static ISolidColorBrush TextPrimary => Solid(Dark ? "#E6E8EC" : "#1F2430");
    public static ISolidColorBrush TextSecondary => Solid(Dark ? "#C7CCD6" : "#4B5563");
    public static ISolidColorBrush TextMuted => Solid(Dark ? "#9AA1AD" : "#6B7280");
    public static ISolidColorBrush TextFaint => Solid(Dark ? "#6B7280" : "#9CA3AF");
    public static ISolidColorBrush ButtonBg => Solid(Dark ? "#353A45" : "#E5E7EB");
    public static ISolidColorBrush Border => Solid(Dark ? "#2E333D" : "#E2E8F0");
    public static ISolidColorBrush Danger => Solid(Dark ? "#EF5350" : "#DC2626");

    private static ISolidColorBrush Solid(string hex) => (ISolidColorBrush)Brush.Parse(hex);
}
