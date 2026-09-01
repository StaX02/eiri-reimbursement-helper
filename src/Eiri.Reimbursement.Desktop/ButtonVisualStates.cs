using System.Windows;
using System.Windows.Media;

namespace Eiri.Reimbursement.Desktop;

public static class ButtonVisualStates
{
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "HoverBackground",
            typeof(Brush),
            typeof(ButtonVisualStates));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "PressedBackground",
            typeof(Brush),
            typeof(ButtonVisualStates));

    public static void SetHoverBackground(DependencyObject element, Brush value) =>
        element.SetValue(HoverBackgroundProperty, value);

    public static Brush? GetHoverBackground(DependencyObject element) =>
        (Brush?)element.GetValue(HoverBackgroundProperty);

    public static void SetPressedBackground(DependencyObject element, Brush value) =>
        element.SetValue(PressedBackgroundProperty, value);

    public static Brush? GetPressedBackground(DependencyObject element) =>
        (Brush?)element.GetValue(PressedBackgroundProperty);
}
