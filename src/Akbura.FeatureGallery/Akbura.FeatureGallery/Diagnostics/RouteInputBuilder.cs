#if DEBUG
using Akbura.Diagnostics;
using Akbura.FeatureGallery.Components;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery.Diagnostics;


/// <summary>
/// Selects one of the routes registered in the Feature Gallery router.
/// </summary>
public sealed class RouteInputBuilder : InputBuilder
{
    public override Type OutputType => typeof(string);

    public override bool CanProvide(InputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.EditorType == typeof(string) &&
               request.ComponentInstance is Router &&
               string.Equals(
                   request.MemberName,
                   nameof(Router.Url),
                   StringComparison.Ordinal);
    }

    protected override Control BuildCore(InputRequest request)
    {
        return BuildCore(
            request,
            request.ExistingValue);
    }

    protected override Control BuildCore(
        InputRequest request,
        object? existingValue)
    {
        var router = (Router)request.ComponentInstance!;
        var routes = GetRoutes(
            router,
            existingValue as string);

        var comboBox = new ComboBox
        {
            ItemsSource = routes,
            MinWidth = 180d,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var binding = new CompiledBinding(InputValueBindingPath)
        {
            Source = comboBox,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger =
                UpdateSourceTrigger.PropertyChanged,
        };

        comboBox.Bind(
            SelectingItemsControl.SelectedItemProperty,
            binding);

        return comboBox;
    }

    private static IReadOnlyList<string> GetRoutes(
        Router router,
        string? currentRoute)
    {
        var routes = new List<string>();
        var knownRoutes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var pages = router.Pages;

        if (pages is not null)
        {
            foreach (var page in pages)
            {
                var route = page.Url;

                if (string.IsNullOrWhiteSpace(route) ||
                    !knownRoutes.Add(route))
                {
                    continue;
                }

                routes.Add(route);
            }
        }

        if (!string.IsNullOrWhiteSpace(currentRoute) &&
            knownRoutes.Add(currentRoute))
        {
            routes.Add(currentRoute);
        }

        return routes;
    }
}
#endif