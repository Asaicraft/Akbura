using Avalonia;
using Avalonia.Controls;

namespace Akbura.UnitTests;

public sealed class AkburaControlExplicitThicknessTests
{
    [Fact]
    public void ExplicitPadding_ComposesHorizontalAndVerticalSidesInAnyOrder()
    {
        var horizontalThenVertical = new Button
        {
            Padding = new Thickness(1, 2, 3, 4),
        };
        AkburaControl.SetExplicitLeftPadding(horizontalThenVertical, 12);
        AkburaControl.SetExplicitRightPadding(horizontalThenVertical, 12);
        AkburaControl.SetExplicitTopPadding(horizontalThenVertical, 8);
        AkburaControl.SetExplicitBottomPadding(horizontalThenVertical, 8);

        var verticalThenHorizontal = new Button
        {
            Padding = new Thickness(1, 2, 3, 4),
        };
        AkburaControl.SetExplicitTopPadding(verticalThenHorizontal, 8);
        AkburaControl.SetExplicitBottomPadding(verticalThenHorizontal, 8);
        AkburaControl.SetExplicitLeftPadding(verticalThenHorizontal, 12);
        AkburaControl.SetExplicitRightPadding(verticalThenHorizontal, 12);

        var expected = new Thickness(12, 8, 12, 8);
        Assert.Equal(expected, horizontalThenVertical.Padding);
        Assert.Equal(expected, verticalThenHorizontal.Padding);
    }

    [Fact]
    public void ExplicitPadding_PreservesBaseValueAndAllowsNegativeSides()
    {
        var button = new Button
        {
            Padding = new Thickness(1, 2, 3, 4),
        };

        Assert.Null(AkburaControl.GetExplicitLeftPadding(button));

        AkburaControl.SetExplicitLeftPadding(button, -6);
        Assert.Equal(-6, AkburaControl.GetExplicitLeftPadding(button));
        Assert.Equal(new Thickness(-6, 2, 3, 4), button.Padding);

        button.Padding = new Thickness(10, 20, 30, 40);
        Assert.Equal(new Thickness(-6, 20, 30, 40), button.Padding);

        button.ClearValue(AkburaControl.ExplicitLeftPaddingProperty);
        Assert.Null(AkburaControl.GetExplicitLeftPadding(button));
        Assert.Equal(new Thickness(10, 20, 30, 40), button.Padding);
    }

    [Fact]
    public void ExplicitMarginAndBorderThickness_UpdateOnlyRequestedSides()
    {
        var border = new Border
        {
            Margin = new Thickness(1, 2, 3, 4),
            BorderThickness = new Thickness(5, 6, 7, 8),
        };

        AkburaControl.SetExplicitTopMargin(border, -2);
        AkburaControl.SetExplicitRightBorderThickness(border, -7);

        Assert.Equal(new Thickness(1, -2, 3, 4), border.Margin);
        Assert.Equal(new Thickness(5, 6, -7, 8), border.BorderThickness);

        border.ClearValue(AkburaControl.ExplicitTopMarginProperty);
        border.ClearValue(AkburaControl.ExplicitRightBorderThicknessProperty);

        Assert.Equal(new Thickness(1, 2, 3, 4), border.Margin);
        Assert.Equal(new Thickness(5, 6, 7, 8), border.BorderThickness);
    }
}
