using Akbura.Akcss;
using Akbura.Language.Syntax;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.CodeAnalysis;
using System.Runtime.Loader;

namespace Akbura.UnitTests;

public sealed partial class AkcssNativeGenerationTests
{
    [Fact]
    public async Task CompiledNativeBodiesAndOldScalarDefinitionsShareTheSameModuleContract()
    {
        var producer = Generate([("Fragments.akcss", Controls + """
            Button.plain { Opacity: 0.35d; }
            """ + "\n" + FragmentUtilities)]);
        var image = producer.Emit();
        using var stream = new MemoryStream(image);
        AssemblyLoadContext.Default.LoadFromStream(stream);
        var metadata = MetadataReference.CreateFromImage(image);
        var markup = FragmentViewMarkup.Replace("Content=\"Other\"", "Content=\"Other\" class=\"plain\"", StringComparison.Ordinal);
        var consumer = Generate([("FragmentView.akbura", markup)], FragmentViewHost, references: [metadata]);
        using var consumerStream = new MemoryStream(consumer.Emit());
        var assembly = AssemblyLoadContext.Default.LoadFromStream(consumerStream);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var type = assembly.GetType("HelloWorld.FragmentView")!;
            var view = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            type.GetMethod("InitializeForTest")!.Invoke(view, null);
            var panel = Assert.IsType<StackPanel>(view.Child);
            var native = Assert.IsType<Button>(panel.Children[0]);
            var scalar = Assert.IsType<Button>(panel.Children[1]);
            var window = new Window { Width = 270d, Height = 180d, Content = view };
            window.Resources["--color-red-300"] = Brushes.Red;
            window.Resources["--color-red-700"] = Brushes.Blue;
            window.Resources["--color-focus"] = Brushes.Green;
            try
            {
                window.Show();
                native.ApplyTemplate();
                scalar.ApplyTemplate();
                PumpFragmentWindow(window);
                Assert.Equal(0.8d, native.Opacity);
                Assert.Equal(0.35d, scalar.Opacity);
                var old = Assert.Single(AkburaControl.GetAkcssStyles(scalar));
                Assert.IsNotAssignableFrom<IAkcssNativeStyleProvider>(old.Style);
                Assert.All(AkburaControl.GetAkcssStyles(native),
                    activator => Assert.IsAssignableFrom<IAkcssNativeStyleProvider>(activator.Style));
                Assert.Equal(Brushes.Red, GetFragmentPresenter(native).Background);
                var inside = native.TranslatePoint(new Point(8d, 8d), window)!.Value;
                window.MouseMove(inside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.Equal(Brushes.Blue, GetFragmentPresenter(native).Background);
                Assert.Equal(1d, native.Opacity);
                Assert.Equal(0.35d, scalar.Opacity);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextualApplyIsDiagnosedThroughTransitiveSourceAndCompiledDependencies(bool compiled)
    {
        var producer = Generate([("Fragments.akcss", FragmentUtilities)]);
        var body = Controls + """
            @using HelloWorld.Fragments.akcss;
            @utilities
            {
                Button.outer { @apply middle; }
                Button.middle { @apply bg-red-300; }
            }
            """;
        var fixture = compiled
            ? Generate([("Apply.akcss", body)], references: [MetadataReference.CreateFromImage(producer.Emit())], validateSemantics: false)
            : Generate([("Fragments.akcss", FragmentUtilities), ("Apply.akcss", body)], validateSemantics: false);
        var errors = fixture.GetGenerationDiagnostics().Where(item => item.Id == ErrorCodes.AKBURA_GENERATION_AkcssUnsupported).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, diagnostic =>
        {
            Assert.Contains("@apply expansion requires native AKCSS application 'bg-red-300'", diagnostic.Message);
            Assert.EndsWith("Apply.akcss", diagnostic.FilePath);
        });
    }

    [Fact]
    public void AnApplicationAnchorRejectsDirectPropertySettersEvenWithAnUnconditionalSourceSelector()
    {
        var fixture = Generate([("Direct.akcss", Controls + """
            @utilities
            {
                (global::HelloWorld.DirectButton).direct-utility
                {
                    Custom: 1d;
                    ^ { Opacity: 0.5d; }
                }
            }
            """)], """
            using Avalonia;
            using Avalonia.Controls;
            namespace HelloWorld;
            public sealed class DirectButton : Button
            {
                public static readonly DirectProperty<DirectButton, double> CustomProperty =
                    AvaloniaProperty.RegisterDirect<DirectButton, double>(nameof(Custom),
                        control => control.Custom, (control, value) => control.Custom = value);
                private double field;
                public double Custom { get => field; set => SetAndRaise(CustomProperty, ref field, value); }
            }
            """);
        var diagnostic = Assert.Single(fixture.GetGenerationDiagnostics());
        Assert.Contains("Direct properties cannot use StyleTrigger priority", diagnostic.Message);
        Assert.EndsWith("Direct.akcss", diagnostic.FilePath);
    }

    [Fact]
    public void KnownIncompatiblePrefixGroupsProduceSourceMappedBuildDiagnostics()
    {
        var markup = FragmentViewMarkup.Replace("${focus}:bg-focus", "${OtherVariant}:bg-focus", StringComparison.Ordinal);
        var fixture = Generate([("Fragments.akcss", FragmentUtilities), ("FragmentView.akbura", markup)],
            FragmentViewHost + """

            [Akbura.Markup.UtilityVariant(1d, ConflictGroup = "other",
                UnprefixedPrecedence = Akbura.Markup.UnprefixedUtilityPrecedence.Above)]
            public sealed class OtherVariantExtension
            {
                public bool ProvideValue() => true;
            }
            """);
        var diagnostic = Assert.Single(fixture.Compilation.GetDiagnostics(),
            item => item.Id == "CS1029" && item.GetMessage().Contains("one group", StringComparison.Ordinal));
        Assert.EndsWith("FragmentView.akbura", diagnostic.Location.GetMappedLineSpan().Path);
    }
}
