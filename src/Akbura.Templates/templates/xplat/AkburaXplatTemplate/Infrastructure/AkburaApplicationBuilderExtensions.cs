using Akbura.Engine;
using Avalonia;

#if ReactiveUIToolkitChosen
using ReactiveUI.Avalonia;
#endif

namespace AkburaTemplateNamespace.Infrastructure;

public static class AkburaApplicationBuilderExtensions
{
    public static AppBuilder UseAkburaApplication(this AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

#if ReactiveUIToolkitChosen
        // AppServices can construct the ViewModel during UseAkbura configuration.
        builder = builder.UseReactiveUI(_ => { });
#endif

#if UseDI
        builder = builder.UseAkbura(akbura =>
        {
            // The designer needs Akbura, but should not instantiate app services.
            if (!Avalonia.Controls.Design.IsDesignMode)
            {
                akbura.WithServiceProvider(AppServices.Current.ServiceProvider);
            }
        });
#else
        builder = builder.UseAkbura();
#endif

        return builder.WithInterFont();
    }
}
