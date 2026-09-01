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

        Assert.Equal(Color.FromRgb(0x11, 0x17, 0x15), BrushColor(resources, "SurfaceBrush"));
        Assert.Equal(Color.FromRgb(0xF1, 0xF6, 0xF4), BrushColor(resources, "PrimaryTextBrush"));
        Assert.Equal(Color.FromRgb(0x58, 0xB2, 0x9D), BrushColor(resources, "PrimaryBrush"));
        Assert.Equal(Color.FromRgb(0x72, 0xC3, 0xB0), BrushColor(resources, "FocusBrush"));
        Assert.Equal(Color.FromRgb(0x11, 0x17, 0x15), BrushColor(resources, "ScrollBarTrackBrush"));
        Assert.Equal(Color.FromRgb(0x52, 0x62, 0x5C), BrushColor(resources, "ScrollBarThumbBrush"));

        ThemeManager.Toggle(resources);

        Assert.Equal(Color.FromRgb(0xF3, 0xF6, 0xF5), BrushColor(resources, "SurfaceBrush"));
        Assert.Equal(Color.FromRgb(0x17, 0x22, 0x1F), BrushColor(resources, "PrimaryTextBrush"));
        Assert.Equal(Color.FromRgb(0x17, 0x6B, 0x5B), BrushColor(resources, "PrimaryBrush"));
        Assert.Equal(Colors.White, BrushColor(resources, "PrimaryButtonTextBrush"));
    }

    private static Color BrushColor(ResourceDictionary resources, string key) =>
        Assert.IsType<SolidColorBrush>(resources[key]).Color;
}
