using Avalonia.Media;
using Avalonia.Threading;
using System.Collections.Generic;

namespace PeekShield.Services;

internal static class Palette
{
    private static readonly Dictionary<string, SolidColorBrush> _brushes = new();

    static Palette()
    {
        ThemeService.Changed += () => ApplyCurrent();
    }

    public static ISolidColorBrush PageBg => Get("PageBg");
    public static ISolidColorBrush CardBg => Get("CardBg");
    public static ISolidColorBrush TextPrimary => Get("TextPrimary");
    public static ISolidColorBrush TextSecondary => Get("TextSecondary");
    public static ISolidColorBrush TextMuted => Get("TextMuted");
    public static ISolidColorBrush TextFaint => Get("TextFaint");
    public static ISolidColorBrush ButtonBg => Get("ButtonBg");
    public static ISolidColorBrush Border => Get("Border");
    public static ISolidColorBrush Danger => Get("Danger");
    public static ISolidColorBrush Accent => Get("Accent");
    public static ISolidColorBrush AccentBg => Get("AccentBg");
    public static ISolidColorBrush AccentFg => Get("AccentFg");

    private static SolidColorBrush Get(string key)
    {
        if (!_brushes.TryGetValue(key, out var b))
        {
            b = new SolidColorBrush(Color.Parse(Hex(key, ThemeService.IsDark, SkinDef.For(ThemeService.Skin))));
            _brushes[key] = b;
        }
        return b;
    }

    private static string Hex(string key, bool dark, SkinDef s) => key switch
    {
        "PageBg" => dark ? "#16161A" : "#EDF0F5",
        "CardBg" => dark ? "#2B2B35" : "#FFFFFF",
        "TextPrimary" => dark ? "#E6E8EC" : "#1F2430",
        "TextSecondary" => dark ? "#C7CCD6" : "#4B5563",
        "TextMuted" => dark ? "#9AA1AD" : "#6B7280",
        "TextFaint" => dark ? "#6B7280" : "#9CA3AF",
        "ButtonBg" => dark ? "#353A45" : "#E5E7EB",
        "Border" => dark ? "#3E4550" : "#E2E8F0",
        "Danger" => dark ? "#EF5350" : "#DC2626",
        "Accent" => dark ? s.DarkAccent : s.LightAccent,
        "AccentBg" => dark ? s.DarkAccentBg : s.LightAccentBg,
        "AccentFg" => dark ? s.DarkAccentFg : s.LightAccentFg,
        _ => "#000000"
    };

    public static void ApplyCurrent()
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyNow();
        else
            Dispatcher.UIThread.Post(ApplyNow);
    }

    private static void ApplyNow()
    {
        var dark = ThemeService.IsDark;
        var s = SkinDef.For(ThemeService.Skin);
        foreach (var kv in _brushes)
            kv.Value.Color = Color.Parse(Hex(kv.Key, dark, s));
    }
}

internal sealed record SkinDef(
    ThemeSkin Skin,
    string Name,
    string LightAccent,
    string LightAccentBg,
    string LightAccentFg,
    string DarkAccent,
    string DarkAccentBg,
    string DarkAccentFg)
{
    public string Preview => LightAccent;

    public static IReadOnlyList<SkinDef> All { get; } = new[]
    {
        new SkinDef(ThemeSkin.Blue,   "默认蓝", "#2563EB", "#DBEAFE", "#1E40AF", "#4C8BF5", "#1E3A8A", "#E6E8EC"),
        new SkinDef(ThemeSkin.Purple, "暗夜紫", "#7C3AED", "#EDE9FE", "#5B21B6", "#A78BFA", "#4C1D95", "#E6E8EC"),
        new SkinDef(ThemeSkin.Green,  "护眼绿", "#059669", "#D1FAE5", "#065F46", "#34D399", "#064E3B", "#E6E8EC"),
        new SkinDef(ThemeSkin.Orange, "活力橙", "#EA580C", "#FFEDD5", "#9A3412", "#FB923C", "#7C2D12", "#1F2430"),
        new SkinDef(ThemeSkin.Rose,   "玫红",   "#E11D48", "#FFE4E6", "#9F1239", "#FB7185", "#881337", "#1F2430"),
        new SkinDef(ThemeSkin.Teal,   "青碧",   "#0D9488", "#CCFBF1", "#115E59", "#2DD4BF", "#134E4A", "#E6E8EC"),
        new SkinDef(ThemeSkin.Slate,  "石墨灰", "#475569", "#E2E8F0", "#1E293B", "#94A3B8", "#334155", "#E6E8EC"),
        new SkinDef(ThemeSkin.Pink,   "樱粉",   "#DB2777", "#FCE7F3", "#9D174D", "#F472B6", "#831843", "#1F2430"),
    };

    public static SkinDef For(ThemeSkin skin) => All.FirstOrDefault(x => x.Skin == skin) ?? All[0];
}
