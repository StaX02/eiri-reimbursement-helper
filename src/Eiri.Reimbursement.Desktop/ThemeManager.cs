using System.Windows;
using System.Windows.Media;

namespace Eiri.Reimbursement.Desktop;

public static class ThemeManager
{
    private const string DarkThemeStateKey = "IsDarkTheme";

    public static void ApplyLightTheme(ResourceDictionary resources) => Apply(
        resources,
        isDark: false,
        primary: "#176B5B",
        primaryHover: "#125749",
        primarySoft: "#E8F3F0",
        surface: "#F3F6F5",
        panel: "#FFFFFF",
        border: "#DDE5E2",
        primaryText: "#17221F",
        secondaryText: "#63716D",
        alternatingRow: "#F9FBFA",
        subtlePanel: "#F7F9F8",
        invoiceDrop: "#EFF7F5",
        supportingDrop: "#F7F9F8",
        input: "#FFFFFF",
        selection: "#E1F0EC",
        hover: "#EDF2F0",
        danger: "#B4473B",
        dangerSoft: "#FCF0EE",
        dangerHover: "#F7DDD9",
        focus: "#238671");

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
            primary: "#58B29D",
            primaryHover: "#72C3B0",
            primarySoft: "#173A33",
            surface: "#111715",
            panel: "#18211E",
            border: "#33413C",
            primaryText: "#F1F6F4",
            secondaryText: "#AAB8B3",
            alternatingRow: "#1B2622",
            subtlePanel: "#202C28",
            invoiceDrop: "#173A33",
            supportingDrop: "#202C28",
            input: "#111715",
            selection: "#23483F",
            hover: "#283630",
            danger: "#F08B80",
            dangerSoft: "#432722",
            dangerHover: "#5A302A",
            focus: "#72C3B0");
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
        string focus)
    {
        resources[DarkThemeStateKey] = isDark;
        SetBrush(resources, "PrimaryBrush", primary);
        SetBrush(resources, "PrimaryHoverBrush", primaryHover);
        SetBrush(resources, "PrimarySoftBrush", primarySoft);
        SetBrush(resources, "SurfaceBrush", surface);
        SetBrush(resources, "PanelBrush", panel);
        SetBrush(resources, "BorderBrush", border);
        SetBrush(resources, "PrimaryTextBrush", primaryText);
        SetBrush(resources, "SecondaryTextBrush", secondaryText);
        SetBrush(resources, "AlternatingRowBrush", alternatingRow);
        SetBrush(resources, "SubtlePanelBrush", subtlePanel);
        SetBrush(resources, "InvoiceDropBrush", invoiceDrop);
        SetBrush(resources, "SupportingDropBrush", supportingDrop);
        SetBrush(resources, "InputBrush", input);
        SetBrush(resources, "SelectionBrush", selection);
        SetBrush(resources, "HoverBrush", hover);
        SetBrush(resources, "DangerBrush", danger);
        SetBrush(resources, "DangerSoftBrush", dangerSoft);
        SetBrush(resources, "DangerHoverBrush", dangerHover);
        SetBrush(resources, "FocusBrush", focus);
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
