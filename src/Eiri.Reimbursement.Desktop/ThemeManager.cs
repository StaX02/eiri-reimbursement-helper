using System.Windows;
using System.Windows.Media;

namespace Eiri.Reimbursement.Desktop;

public static class ThemeManager
{
    private const string DarkThemeStateKey = "IsDarkTheme";

    public static void ApplyLightTheme(ResourceDictionary resources) => Apply(
        resources,
        isDark: false,
        primary: "#315B7D",
        surface: "#F6F7F9",
        panel: "#FFFFFF",
        border: "#E4E7EC",
        primaryText: "#101828",
        secondaryText: "#667085",
        alternatingRow: "#FAFAFA",
        subtlePanel: "#F9FAFB",
        invoiceDrop: "#F0F7FF",
        supportingDrop: "#F8FAFC",
        input: "#FFFFFF");

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
            primary: "#4F8BB3",
            surface: "#111827",
            panel: "#1F2937",
            border: "#374151",
            primaryText: "#F9FAFB",
            secondaryText: "#CBD5E1",
            alternatingRow: "#253044",
            subtlePanel: "#273449",
            invoiceDrop: "#1E3A5F",
            supportingDrop: "#263244",
            input: "#111827");
    }

    private static void Apply(
        ResourceDictionary resources,
        bool isDark,
        string primary,
        string surface,
        string panel,
        string border,
        string primaryText,
        string secondaryText,
        string alternatingRow,
        string subtlePanel,
        string invoiceDrop,
        string supportingDrop,
        string input)
    {
        resources[DarkThemeStateKey] = isDark;
        SetBrush(resources, "PrimaryBrush", primary);
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
    }

    private static void SetBrush(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
