using System.Windows;
using System.Windows.Media;
using Eiri.Reimbursement.Desktop;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class ThemeManagerTests
{
    [Fact]
    public void TogglesBetweenDarkAndLightThemeResources()
    {
        ResourceDictionary resources = [];
        ThemeManager.ApplyLightTheme(resources);

        ThemeManager.Toggle(resources);

        Assert.Equal(Color.FromRgb(0x11, 0x18, 0x27), BrushColor(resources, "SurfaceBrush"));
        Assert.Equal(Color.FromRgb(0xF9, 0xFA, 0xFB), BrushColor(resources, "PrimaryTextBrush"));

        ThemeManager.Toggle(resources);

        Assert.Equal(Color.FromRgb(0xF6, 0xF7, 0xF9), BrushColor(resources, "SurfaceBrush"));
        Assert.Equal(Color.FromRgb(0x10, 0x18, 0x28), BrushColor(resources, "PrimaryTextBrush"));
    }

    private static Color BrushColor(ResourceDictionary resources, string key) =>
        Assert.IsType<SolidColorBrush>(resources[key]).Color;
}
