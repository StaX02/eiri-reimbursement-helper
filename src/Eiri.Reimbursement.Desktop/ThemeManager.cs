using System.Windows;
using System.Windows.Media;

namespace Eiri.Reimbursement.Desktop;

public static class ThemeManager
{
    private const string DarkThemeStateKey = "IsDarkTheme";

    public static void ApplyLightTheme(ResourceDictionary resources) => Apply(
        resources,
        isDark: false,
        primary: "#AE4525",
        primaryHover: "#90391F",
        primarySoft: "#F8E8DC",
        surface: "#F2F0E7",
        panel: "#FFFDF7",
        border: "#D8D4C7",
        primaryText: "#292D2B",
        primaryButtonText: "#FFFDF7",
        secondaryText: "#66685F",
        alternatingRow: "#F8F6EE",
        subtlePanel: "#F5F2E8",
        invoiceDrop: "#F9EDE2",
        supportingDrop: "#F5F2E8",
        input: "#FFFDF7",
        selection: "#E0EBE4",
        hover: "#ECE9DD",
        danger: "#922F27",
        dangerSoft: "#F8E8E2",
        dangerHover: "#F0D4CB",
        focus: "#AE4525",
        scrollBarTrack: "#F5F2E8",
        scrollBarThumb: "#8B8E81",
        scrollBarThumbHover: "#66685F");

    public static void Toggle(ResourceDictionary resources)
    {
        if (resources[DarkThemeStateKey] is true)
        {
            ApplyLightTheme(resources);
            return;
        }

        Apply(
            resources,
            isDark: true,
            primary: "#ED986F",
            primaryHover: "#F5B38F",
            primarySoft: "#403027",
            surface: "#1C211F",
            panel: "#262C28",
            border: "#495047",
            primaryText: "#F6F1E3",
            primaryButtonText: "#252A26",
            secondaryText: "#B8BDB0",
            alternatingRow: "#2A312C",
            subtlePanel: "#30372F",
            invoiceDrop: "#403027",
            supportingDrop: "#30372F",
            input: "#1C211F",
            selection: "#344C42",
            hover: "#3A4137",
            danger: "#F09B8E",
            dangerSoft: "#492E29",
            dangerHover: "#603930",
            focus: "#F5B38F",
            scrollBarTrack: "#1C211F",
            scrollBarThumb: "#788273",
            scrollBarThumbHover: "#A1AE9B");
    }

    private static void Apply(
        ResourceDictionary resources,
        bool isDark,
        string primary,
        string primaryHover,
        string primarySoft,
        string surface,
        string panel,
        string border,
        string primaryText,
        string primaryButtonText,
        string secondaryText,
        string alternatingRow,
        string subtlePanel,
        string invoiceDrop,
        string supportingDrop,
        string input,
        string selection,
        string hover,
        string danger,
        string dangerSoft,
        string dangerHover,
        string focus,
        string scrollBarTrack,
        string scrollBarThumb,
        string scrollBarThumbHover)
    {
        resources[DarkThemeStateKey] = isDark;
        SetBrush(resources, "PrimaryBrush", primary);
        SetBrush(resources, "AccentBrush", isDark ? "#ED986F" : "#E56833");
        SetBrush(resources, "SuccessBrush", isDark ? "#83B9A9" : "#326D5E");
        SetBrush(resources, "PrimaryHoverBrush", primaryHover);
        SetBrush(resources, "PrimarySoftBrush", primarySoft);
        SetBrush(resources, "SurfaceBrush", surface);
        SetBrush(resources, "PanelBrush", panel);
        SetBrush(resources, "BorderBrush", border);
        SetBrush(resources, "PrimaryTextBrush", primaryText);
        SetBrush(resources, "PrimaryButtonTextBrush", primaryButtonText);
        SetBrush(resources, "SecondaryTextBrush", secondaryText);
        SetBrush(resources, "AlternatingRowBrush", alternatingRow);
        SetBrush(resources, "SubtlePanelBrush", subtlePanel);
        SetBrush(resources, "InvoiceDropBrush", invoiceDrop);
        SetBrush(resources, "SupportingDropBrush", supportingDrop);
        SetBrush(resources, "InputBrush", input);
        SetBrush(resources, "SelectionBrush", selection);
        SetBrush(resources, "HoverBrush", hover);
        SetBrush(resources, "DangerBrush", danger);
        SetBrush(resources, "WarningBrush", isDark ? "#E5B54F" : "#855B11");
        SetBrush(resources, "DangerSoftBrush", dangerSoft);
        SetBrush(resources, "DangerHoverBrush", dangerHover);
        SetBrush(resources, "FocusBrush", focus);
        SetBrush(resources, "ScrollBarTrackBrush", scrollBarTrack);
        SetBrush(resources, "ScrollBarThumbBrush", scrollBarThumb);
        SetBrush(resources, "ScrollBarThumbHoverBrush", scrollBarThumbHover);
        SetSystemBrush(resources, SystemColors.WindowBrushKey, input);
        SetSystemBrush(resources, SystemColors.WindowTextBrushKey, primaryText);
        SetSystemBrush(resources, SystemColors.ControlBrushKey, panel);
        SetSystemBrush(resources, SystemColors.ControlTextBrushKey, primaryText);
        SetSystemBrush(resources, SystemColors.HighlightBrushKey, selection);
        SetSystemBrush(resources, SystemColors.HighlightTextBrushKey, primaryText);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private static void SetSystemBrush(ResourceDictionary resources, ResourceKey key, string color) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
