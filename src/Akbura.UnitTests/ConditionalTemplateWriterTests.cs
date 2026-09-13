using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class ConditionalTemplateWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalDeferredTemplate_PreservesTypedAncestorItemPerInstance(bool structural)
    {
        var type = Compile(
            """
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Group" x.ItemName="group">
                    <ItemsControl>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border>$if (Expanded) { <TextBlock Text={group.Name} /> }</Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var outer = Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ItemsControl>(owner.Child).ItemTemplate);
                var groupType = type.Assembly.GetType("Demo.Group")!;
                var firstGroup = Activator.CreateInstance(groupType)!;
                var secondGroup = Activator.CreateInstance(groupType)!;
                Set(firstGroup, "Name", "first group");
                Set(secondGroup, "Name", "second group");
                var firstHost = Assert.IsType<ItemsControl>(outer.Build(firstGroup));
                var secondHost = Assert.IsType<ItemsControl>(outer.Build(secondGroup));
                panel.Children.Add(firstHost);
                panel.Children.Add(secondHost);
                var firstTemplate = Assert.IsAssignableFrom<IDataTemplate>(firstHost.ItemTemplate);
                var secondTemplate = Assert.IsAssignableFrom<IDataTemplate>(secondHost.ItemTemplate);
                var first = Assert.IsType<Border>(firstTemplate.Build(null));
                var second = Assert.IsType<Border>(secondTemplate.Build(null));
                panel.Children.Add(first);
                panel.Children.Add(second);
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                Assert.Equal("first group", firstText.Text);
                Assert.Equal("second group", secondText.Text);

                Set(firstGroup, "Name", "first changed");
                Set(secondGroup, "Name", "second changed");
                owner.InvalidState();
                Assert.Same(firstTemplate, firstHost.ItemTemplate);
                Assert.Same(secondTemplate, secondHost.ItemTemplate);
                Assert.Same(firstText, first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal("first changed", firstText.Text);
                Assert.Equal("second changed", secondText.Text);
                Set(owner, "Expanded", false);
                owner.InvalidState();
                Assert.Null(first.Child);
                Assert.Null(second.Child);
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
    public async Task EnclosingConditionCapture_DoesNotKeepModelAliveAfterBranchExit(bool structural)
    {
        var type = Compile(
            """
            <StackPanel>
                $if (Model is Person current)
                {
                    <ItemsControl>
                        <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="item">
                            <Border>$if (Expanded) { <TextBlock Text={current.Name + ": " + item.Name} /> }</Border>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                }
            </StackPanel>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var (instance, model) = BuildCapturedModelInstance(type, owner);
                panel.Children.Add(instance);
                Assert.Equal("captured: item", Assert.IsType<TextBlock>(instance.Child).Text);
                Set(owner, "Model", null);
                owner.InvalidState();
                Assert.Empty(Assert.IsType<StackPanel>(owner.Child).Children);
                Assert.Null(instance.Child);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Assert.False(model.IsAlive);
                GC.KeepAlive(instance);
                GC.KeepAlive(owner);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Border Instance, WeakReference Model) BuildCapturedModelInstance(Type type, AkburaControl owner)
    {
        var model = NewPerson(type, "captured", true);
        Set(owner, "Model", model);
        owner.InvalidState();
        var host = Assert.IsType<ItemsControl>(Assert.Single(Assert.IsType<StackPanel>(owner.Child).Children));
        var template = Assert.IsAssignableFrom<IDataTemplate>(host.ItemTemplate);
        var instance = Assert.IsType<Border>(template.Build(NewPerson(type, "item", true)));
        return (instance, new WeakReference(model));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task EnclosingConditionCapture_RefreshesRetainedTemplateInstances(
        bool structural, bool deferred, bool outVariable)
    {
        var condition = outVariable ? "TryPerson(out var current)" : "Model is Person current";
        var content = deferred
            ? "<ItemsControl.ItemTemplate><DataTemplate><Border>$if (Expanded) { <TextBlock Text={current.Name} /> }</Border></DataTemplate></ItemsControl.ItemTemplate>"
            : "<ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"item\"><Border>$if (Expanded) { <TextBlock Text={current.Name + \": \" + item.Name} /> }</Border></ItemsControl.ItemTemplate>";
        var type = Compile("<StackPanel>$if (" + condition + ") { <ItemsControl>" + content +
            "</ItemsControl> }</StackPanel>", structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            Set(owner, "Model", NewPerson(type, "original", true));
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var host = Assert.IsType<ItemsControl>(Assert.Single(root.Children));
                var template = Assert.IsAssignableFrom<IDataTemplate>(host.ItemTemplate);
                var first = Assert.IsType<Border>(template.Build(NewPerson(type, "first", true)));
                var second = Assert.IsType<Border>(template.Build(NewPerson(type, "second", true)));
                panel.Children.Add(first);
                panel.Children.Add(second);
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                Assert.NotSame(firstText, secondText);
                Assert.Equal(deferred ? "original" : "original: first", firstText.Text);
                Assert.Equal(deferred ? "original" : "original: second", secondText.Text);

                Set(owner, "Model", NewPerson(type, "updated", true));
                owner.InvalidState();
                Assert.Same(host, Assert.Single(root.Children));
                Assert.Same(template, host.ItemTemplate);
                Assert.Same(firstText, first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal(deferred ? "updated" : "updated: first", firstText.Text);
                Assert.Equal(deferred ? "updated" : "updated: second", secondText.Text);

                Set(owner, "Expanded", false);
                owner.InvalidState();
                Assert.Null(first.Child);
                Assert.Null(second.Child);
                Set(owner, "Expanded", true);
                owner.InvalidState();
                Assert.NotSame(firstText, first.Child);
                Assert.NotSame(secondText, second.Child);
                Assert.Equal(deferred ? "updated" : "updated: first", Assert.IsType<TextBlock>(first.Child).Text);

                Set(owner, "Model", null);
                owner.InvalidState();
                Assert.Empty(root.Children);
                Assert.Null(first.Child);
                Assert.Null(second.Child);
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
    public async Task OutVariableFromFalseCondition_RemainsCurrentInElseTemplate(bool structural)
    {
        var type = Compile(
            """
            <StackPanel>
                $if (TryPerson(out var current)) { <TextBlock Text="unused" /> }
                $else
                {
                    <ItemsControl>
                        <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="item">
                            <Border>$if (Expanded) { <TextBlock Text={current.Name + ": " + item.Name} /> }</Border>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                }
            </StackPanel>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            Set(owner, "Model", NewPerson(type, "original", true));
            Set(owner, "CanUsePerson", false);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var host = Assert.IsType<ItemsControl>(Assert.Single(root.Children));
                var template = Assert.IsAssignableFrom<IDataTemplate>(host.ItemTemplate);
                var child = Assert.IsType<Border>(template.Build(NewPerson(type, "item", true)));
                panel.Children.Add(child);
                var text = Assert.IsType<TextBlock>(child.Child);
                Assert.Equal("original: item", text.Text);
                Set(owner, "Model", NewPerson(type, "updated", true));
                owner.InvalidState();
                Assert.Same(host, Assert.Single(root.Children));
                Assert.Same(template, host.ItemTemplate);
                Assert.Same(text, child.Child);
                Assert.Equal("updated: item", text.Text);
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
    public async Task NestedLocalConditionCapture_RemainsIsolatedBetweenOuterInstances(bool structural)
    {
        var type = Compile(
            """
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Group" x.ItemName="group">
                    <Border>
                        $if (group.Name is { } current)
                        {
                            <ItemsControl>
                                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="item">
                                    <Border>$if (Expanded) { <TextBlock Text={current + ": " + item.Name} /> }</Border>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var template = Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ItemsControl>(owner.Child).ItemTemplate);
                var groupType = type.Assembly.GetType("Demo.Group")!;
                var firstGroup = Activator.CreateInstance(groupType)!;
                var secondGroup = Activator.CreateInstance(groupType)!;
                Set(firstGroup, "Name", "first group");
                Set(secondGroup, "Name", "second group");
                var first = Assert.IsType<Border>(template.Build(firstGroup));
                var second = Assert.IsType<Border>(template.Build(secondGroup));
                panel.Children.Add(first);
                panel.Children.Add(second);
                var firstHost = Assert.IsType<ItemsControl>(first.Child);
                var secondHost = Assert.IsType<ItemsControl>(second.Child);
                var firstTemplate = Assert.IsAssignableFrom<IDataTemplate>(firstHost.ItemTemplate);
                var secondTemplate = Assert.IsAssignableFrom<IDataTemplate>(secondHost.ItemTemplate);
                var firstChild = Assert.IsType<Border>(firstTemplate.Build(NewPerson(type, "one", true)));
                var secondChild = Assert.IsType<Border>(secondTemplate.Build(NewPerson(type, "two", true)));
                panel.Children.Add(firstChild);
                panel.Children.Add(secondChild);
                var firstText = Assert.IsType<TextBlock>(firstChild.Child);
                var secondText = Assert.IsType<TextBlock>(secondChild.Child);
                Assert.Equal("first group: one", firstText.Text);
                Assert.Equal("second group: two", secondText.Text);

                Set(firstGroup, "Name", "first changed");
                Set(secondGroup, "Name", "second changed");
                owner.InvalidState();
                Assert.Same(firstHost, first.Child);
                Assert.Same(secondHost, second.Child);
                Assert.Same(firstTemplate, firstHost.ItemTemplate);
                Assert.Same(secondTemplate, secondHost.ItemTemplate);
                Assert.Same(firstText, firstChild.Child);
                Assert.Same(secondText, secondChild.Child);
                Assert.Equal("first changed: one", firstText.Text);
                Assert.Equal("second changed: two", secondText.Text);
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
    public async Task MixedTemplates_OrdinarySiblingFactoryKeepsItsRenderLocalScope(bool structural)
    {
        var type = Compile(
            """
            var prefix = Text;
            <StackPanel>
                <ItemsControl>
                    <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="ordinary">
                        <TextBlock Text={prefix + ": " + ordinary.Name} />
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ItemsControl>
                    <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="conditional">
                        <Border>
                            $if (Evaluate(conditional)) { <TextBlock Text={conditional.Name} /> }
                        </Border>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                Assert.Equal(2, root.Children.Count);
                var ordinaryOwner = Assert.IsType<ItemsControl>(root.Children[0]);
                var conditionalOwner = Assert.IsType<ItemsControl>(root.Children[1]);
                var ordinaryTemplate = Assert.IsAssignableFrom<IDataTemplate>(ordinaryOwner.ItemTemplate);
                var conditionalTemplate = Assert.IsAssignableFrom<IDataTemplate>(conditionalOwner.ItemTemplate);
                var ordinary = Assert.IsType<TextBlock>(ordinaryTemplate.Build(NewPerson(type, "ordinary", true)));
                var conditional = Assert.IsType<Border>(conditionalTemplate.Build(NewPerson(type, "conditional", true)));
                root.Children.Add(ordinary);
                root.Children.Add(conditional);
                var retained = Assert.IsType<TextBlock>(conditional.Child);
                Assert.Equal("initial: ordinary", ordinary.Text);
                Assert.Equal("conditional", retained.Text);
                Assert.Equal(1, Read<int>(owner, "ConditionEvaluations"));

                owner.InvalidState();
                Assert.Same(ordinaryTemplate, ordinaryOwner.ItemTemplate);
                Assert.Same(conditionalTemplate, conditionalOwner.ItemTemplate);
                Assert.Same(retained, conditional.Child);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConditionalTemplateRoot_UsesNativeHostAndRetainsIndependentInstances(bool deferred, bool structural)
    {
        var branches =
            """
            $if (person != null && Evaluate(person)) { <TextBlock Text={prefix + ": " + person.Name} /> }
            $else if (person is { Mode: 2 }) { <Button Content={prefix + ": " + person.Name} /> }
            """;
        var template = deferred
            ? "<ContentControl.ContentTemplate><DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
                branches + "</DataTemplate></ContentControl.ContentTemplate>"
            : "<ContentControl.ContentTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
                branches + "</ContentControl.ContentTemplate>";
        var type = Compile("var prefix = Text; <ContentControl>" + template + "</ContentControl>", structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var destination = Assert.IsType<ContentControl>(owner.Child);
                var descriptor = Assert.IsAssignableFrom<IDataTemplate>(destination.ContentTemplate);
                var firstPerson = NewPerson(type, "first", false);
                var secondPerson = NewPerson(type, "second", false);
                var first = new ContentPresenter { Content = firstPerson, ContentTemplate = descriptor };
                var second = new ContentPresenter { Content = secondPerson, ContentTemplate = descriptor };
                panel.Children.Add(first);
                panel.Children.Add(second);
                first.UpdateChild();
                second.UpdateChild();
                Assert.Null(first.Child);
                Assert.Null(second.Child);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));

                Set(firstPerson, "Selected", true);
                Set(secondPerson, "Selected", true);
                owner.InvalidState();
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                Assert.NotSame(firstText, secondText);
                Assert.Equal("initial: first", firstText.Text);
                Assert.Equal("initial: second", secondText.Text);
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Text", "updated");
                Set(firstPerson, "Name", "first updated");
                owner.InvalidState();
                Assert.Same(descriptor, destination.ContentTemplate);
                Assert.Same(firstText, first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal("updated: first updated", firstText.Text);
                Assert.Equal("updated: second", secondText.Text);
                Assert.Equal(6, Read<int>(owner, "ConditionEvaluations"));

                Set(firstPerson, "Selected", false);
                Set(firstPerson, "Mode", 2);
                owner.InvalidState();
                var button = Assert.IsType<Button>(first.Child);
                Assert.Equal("updated: first updated", button.Content);
                Assert.Same(secondText, second.Child);
                Assert.Equal(8, Read<int>(owner, "ConditionEvaluations"));

                Set(firstPerson, "Mode", 0);
                owner.InvalidState();
                Assert.Null(first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal(10, Read<int>(owner, "ConditionEvaluations"));

                Set(firstPerson, "Selected", true);
                owner.InvalidState();
                var remounted = Assert.IsType<TextBlock>(first.Child);
                Assert.NotSame(firstText, remounted);
                Assert.Equal("updated: first updated", remounted.Text);
                Assert.Same(secondText, second.Child);
                Assert.Equal(12, Read<int>(owner, "ConditionEvaluations"));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void ConditionalCompleteTemplateObjects_AreNotRejectedAsControlRootReplacement()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;
            using Demo;
            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    $if (Expanded) { <DataTemplate><TextBlock /></DataTemplate> }
                    $else { <DataTemplate><Button /></DataTemplate> }
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, OwnerSource);

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConditionalTemplateRoot_UsesActualRootServicesAndStopsWhenOuterOwnerExits(bool deferred, bool structural)
    {
        var branches =
            """
            $if (person != null && Evaluate(person))
            {
                <TextBlock Text=${ContextProbe} Tag={current.Name + ": " + person.Name} />
            }
            """;
        var template = deferred
            ? "<ContentControl.ContentTemplate><DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
                branches + "</DataTemplate></ContentControl.ContentTemplate>"
            : "<ContentControl.ContentTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
                branches + "</ContentControl.ContentTemplate>";
        var type = Compile("<StackPanel>$if (Model is Person current) { <ContentControl>" + template +
            "</ContentControl> }</StackPanel>", structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            Set(owner, "Model", NewPerson(type, "outer", true));
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var ownerPanel = Assert.IsType<StackPanel>(owner.Child);
                var destination = Assert.IsType<ContentControl>(Assert.Single(ownerPanel.Children));
                var descriptor = Assert.IsAssignableFrom<IDataTemplate>(destination.ContentTemplate);
                var first = new ContentPresenter
                {
                    Content = NewPerson(type, "first", true),
                    ContentTemplate = descriptor,
                };
                var second = new ContentPresenter
                {
                    Content = NewPerson(type, "second", true),
                    ContentTemplate = descriptor,
                };
                panel.Children.Add(first);
                panel.Children.Add(second);
                first.UpdateChild();
                second.UpdateChild();
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                Assert.Equal("outer: first", firstText.Tag);
                Assert.Equal("outer: second", secondText.Tag);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));
                var snapshots = Assert.IsAssignableFrom<System.Collections.IList>(type.Assembly
                    .GetType("Demo.ContextProbeExtension")!.GetProperty("Snapshots")!.GetValue(null));
                Assert.Equal(2, snapshots.Count);
                foreach (var snapshot in snapshots.Cast<object>())
                {
                    var target = Read<object>(snapshot, "Target");
                    Assert.Contains(target, new object[] { firstText, secondText });
                    Assert.Same(owner, Read<object>(snapshot, "Root"));
                    Assert.Same(target, Read<object>(snapshot, "IntermediateRoot"));
                    Assert.Same(TextBlock.TextProperty, Read<object>(snapshot, "Property"));
                    var parents = Read<object[]>(snapshot, "Parents");
                    Assert.Contains(owner, parents);
                    Assert.Contains(destination, parents);
                    Assert.Contains(target, parents);
                    Assert.DoesNotContain(parents, parent => parent is Akbura.Markup.AkburaConditionalTemplateInstance);
                }

                Set(owner, "Model", NewPerson(type, "changed outer", true));
                owner.InvalidState();
                Assert.Same(destination, Assert.Single(ownerPanel.Children));
                Assert.Same(descriptor, destination.ContentTemplate);
                Assert.Same(firstText, first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal("changed outer: first", firstText.Tag);
                Assert.Equal("changed outer: second", secondText.Tag);
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));
                Assert.Equal(2, snapshots.Count);

                Set(owner, "Model", null);
                owner.InvalidState();
                Assert.Empty(ownerPanel.Children);
                var evaluations = Read<int>(owner, "ConditionEvaluations");
                owner.InvalidState();
                first.UpdateChild();
                second.UpdateChild();
                Assert.Equal(evaluations, Read<int>(owner, "ConditionEvaluations"));
                Assert.Equal(2, snapshots.Count);
                GC.KeepAlive(first);
                GC.KeepAlive(second);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConditionalTemplate_NullableOutCapturePreservesLegalNullAssignments(bool trueBranch, bool structural)
    {
        var templateSource =
            """
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                    <Border>
                        $if (Expanded)
                        {
                            <TextBlock Text={(current = null)?.Name ?? "missing"} />
                        }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        if (trueBranch)
        {
            templateSource = templateSource.Replace("(current = null)?.Name ?? \"missing\"",
                "nameof(current) + \": \" + current.Name + \": \" + ((current = null)?.Name ?? \"missing\") + current?.Name");
        }

        var content = trueBranch
            ? "$if (TryOptionalPerson(out var current)) { " + templateSource + " } $else { <TextBlock /> }"
            : "$if (TryOptionalPerson(out var current)) { <TextBlock /> } $else { " + templateSource + " }";
        var type = Compile("<StackPanel>" + content + "</StackPanel>", structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            if (trueBranch)
            {
                Set(owner, "Model", NewPerson(type, "true branch", true));
            }
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var destination = Assert.IsType<ItemsControl>(Assert.Single(Assert.IsType<StackPanel>(owner.Child).Children));
                var template = Assert.IsAssignableFrom<IDataTemplate>(destination.ItemTemplate);
                var root = Assert.IsType<Border>(template.Build(NewPerson(type, "item", true)));
                panel.Children.Add(root);
                var text = Assert.IsType<TextBlock>(root.Child);
                Assert.Equal(trueBranch ? "current: true branch: missing" : "missing", text.Text);
                Set(owner, "Model", NewPerson(type, "nullable false-branch value", true));
                Set(owner, "CanUsePerson", trueBranch);
                owner.InvalidState();
                Assert.Same(root, panel.Children[1]);
                Assert.Same(text, root.Child);
                Assert.Equal(trueBranch ? "current: nullable false-branch value: missing" : "missing", text.Text);
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
    public async Task ConditionalTemplateRoot_AllowsNullableControlExpressionsWithoutAWrapper(bool structural)
    {
        var type = Compile(
            """
            <ContentControl>
                <ContentControl.ContentTemplate x.DataType="Person" x.ItemName="person">
                    $if (person is { Selected: true }) { {SuppliedRoot} }
                </ContentControl.ContentTemplate>
            </ContentControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var destination = Assert.IsType<ContentControl>(owner.Child);
                var person = NewPerson(type, "first", true);
                var presenter = new ContentPresenter { Content = person, ContentTemplate = destination.ContentTemplate };
                presenter.UpdateChild();
                Assert.Null(presenter.Child);
                var first = new TextBlock { Text = "supplied first" };
                Set(owner, "SuppliedRoot", first);
                owner.InvalidState();
                Assert.Same(first, presenter.Child);
                owner.InvalidState();
                Assert.Same(first, presenter.Child);
                var second = new Button { Content = "supplied second" };
                Set(owner, "SuppliedRoot", second);
                owner.InvalidState();
                Assert.Same(second, presenter.Child);
                Set(owner, "SuppliedRoot", null);
                owner.InvalidState();
                Assert.Null(presenter.Child);
                Set(person, "Selected", false);
                Set(owner, "SuppliedRoot", first);
                owner.InvalidState();
                Assert.Null(presenter.Child);
                GC.KeepAlive(presenter);
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
    public async Task ConditionalTemplateRoot_NativeNullMatchDoesNotEvaluateGuardedDataOrConstructInactiveRoot(bool structural)
    {
        var type = Compile(
            """
            <ContentControl>
                <ContentControl.ContentTemplate x.DataType="Person" x.ItemName="person">
                    $if (person != null && Evaluate(person)) { <CountingTextBlock /> }
                </ContentControl.ContentTemplate>
            </ContentControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var destination = Assert.IsType<ContentControl>(owner.Child);
                var descriptor = Assert.IsAssignableFrom<IDataTemplate>(destination.ContentTemplate);
                Assert.True(descriptor.Match(null));
                var presenter = new ContentPresenter { Content = null, ContentTemplate = descriptor };
                presenter.UpdateChild();
                Assert.Null(presenter.Child);
                owner.InvalidState();
                Assert.Null(presenter.Child);
                Assert.Equal(0, Read<int>(owner, "ConditionEvaluations"));
                Assert.Equal(0, type.Assembly.GetType("Demo.CountingTextBlock")!
                    .GetProperty("Created")!.GetValue(null));
                GC.KeepAlive(presenter);
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
    public async Task ConditionalControlTemplateRoot_UsesNativeVisualRootAndCurrentParentCaptures(bool structural)
    {
        var type = Compile(
            """
            var title = Text;
            <StackPanel>
                <Button Content="foreign sibling" />
                <Button>
                    <Button.Template>
                        <ControlTemplate>
                            $if (EvaluateControl()) { <TextBlock Text={title} /> }
                            $else if (AlternateRoot) { <Border /> }
                        </ControlTemplate>
                    </Button.Template>
                </Button>
            </StackPanel>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            Set(owner, "Expanded", false);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var panel = Assert.IsType<StackPanel>(owner.Child);
                var sibling = Assert.IsType<Button>(panel.Children[0]);
                var host = Assert.IsType<Button>(panel.Children[1]);
                host.ApplyTemplate();
                Assert.Empty(host.GetVisualChildren());
                Assert.Equal(1, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Expanded", true);
                owner.InvalidState();
                var first = Assert.IsType<TextBlock>(Assert.Single(host.GetVisualChildren()));
                Assert.Equal("initial", first.Text);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Text", "updated");
                owner.InvalidState();
                Assert.Same(first, Assert.Single(host.GetVisualChildren()));
                Assert.Equal("updated", first.Text);
                Assert.Equal(3, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Expanded", false);
                Set(owner, "AlternateRoot", true);
                owner.InvalidState();
                Assert.IsType<Border>(Assert.Single(host.GetVisualChildren()));
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "AlternateRoot", false);
                owner.InvalidState();
                Assert.Empty(host.GetVisualChildren());
                Assert.Equal(5, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Expanded", true);
                owner.InvalidState();
                var remounted = Assert.IsType<TextBlock>(Assert.Single(host.GetVisualChildren()));
                Assert.NotSame(first, remounted);
                Assert.Equal("updated", remounted.Text);
                Assert.Equal(6, Read<int>(owner, "ConditionEvaluations"));
                Assert.Same(sibling, panel.Children[0]);
                Assert.Same(host, panel.Children[1]);
                Assert.Equal("foreign sibling", sibling.Content);
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
    public async Task TypedTemplate_RenderLocalCapturesRefreshWithoutReplacingRetainedControls(bool structural)
    {
        var type = Compile(
            """
            var prefix = Text;
            string? optional = Expanded ? Text : null;
            prefix += " final";
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                    <Border>
                        $if (optional is { } current)
                        {
                            <TextBlock Text={prefix + ": " + person.Name + ": " + current} />
                        }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var template = Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ItemsControl>(owner.Child).ItemTemplate);
                var first = Assert.IsType<Border>(template.Build(NewPerson(type, "first", true)));
                var second = Assert.IsType<Border>(template.Build(NewPerson(type, "second", true)));
                panel.Children.Add(first);
                panel.Children.Add(second);
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                Assert.Equal("initial final: first: initial", firstText.Text);
                Assert.Equal("initial final: second: initial", secondText.Text);

                Set(owner, "Text", "updated");
                owner.InvalidState();
                Assert.Same(firstText, first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal("updated final: first: updated", firstText.Text);
                Assert.Equal("updated final: second: updated", secondText.Text);

                Set(owner, "Expanded", false);
                owner.InvalidState();
                Assert.Null(first.Child);
                Assert.Null(second.Child);
                Set(owner, "Expanded", true);
                owner.InvalidState();
                Assert.NotSame(firstText, first.Child);
                Assert.NotSame(secondText, second.Child);
                Assert.Equal("updated final: first: updated", Assert.IsType<TextBlock>(first.Child).Text);
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
    public async Task TypedTemplate_InstancesRetainIndependentConditionalControls(bool structural)
    {
        var type = Compile(
            """
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                    <Border>
                        $if (Evaluate(person)) { <TextBox Text={person.Name} /> }
                        $else { <Button Content={person.Name} /> }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var template = Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ItemsControl>(owner.Child).ItemTemplate);
                var firstItem = NewPerson(type, "first", true);
                var secondItem = NewPerson(type, "second", false);
                var first = Assert.IsType<Border>(template.Build(firstItem));
                var second = Assert.IsType<Border>(template.Build(secondItem));
                panel.Children.Add(first);
                panel.Children.Add(second);
                var firstText = Assert.IsType<TextBox>(first.Child);
                var secondButton = Assert.IsType<Button>(second.Child);
                Assert.Equal("first", firstText.Text);
                Assert.Equal("second", secondButton.Content);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));

                Set(firstItem, "Name", "updated first");
                owner.InvalidState();
                Assert.Same(firstText, first.Child);
                Assert.Same(secondButton, second.Child);
                Assert.Equal("updated first", firstText.Text);
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));

                Set(firstItem, "Selected", false);
                owner.InvalidState();
                Assert.NotSame(firstText, first.Child);
                Assert.Equal("updated first", Assert.IsType<Button>(first.Child).Content);
                Assert.Same(secondButton, second.Child);
                Set(firstItem, "Selected", true);
                owner.InvalidState();
                Assert.NotSame(firstText, first.Child);
                Assert.Equal("updated first", Assert.IsType<TextBox>(first.Child).Text);
                Assert.Equal(8, Read<int>(owner, "ConditionEvaluations"));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConditionalTemplate_MarkupExtensionContextUsesInstanceParentsAndParentResources(
        bool deferred, bool structural)
    {
        var content =
            """
            <Border>
                $if (Expanded)
                {
                    <TextBlock Text=${ContextProbe} Background=${StaticResource AccentBrush} />
                }
            </Border>
            """;
        var templateSource = deferred
            ? "<ItemsControl.ItemTemplate><DataTemplate>" + content + "</DataTemplate></ItemsControl.ItemTemplate>"
            : "<ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" + content + "</ItemsControl.ItemTemplate>";
        var type = Compile("using Avalonia.Media; <ItemsControl><ItemsControl.Resources>" +
            "<SolidColorBrush x.key=\"AccentBrush\" Color=\"Red\" />" +
            "</ItemsControl.Resources>" + templateSource + "</ItemsControl>", structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var items = Assert.IsType<ItemsControl>(owner.Child);
                var template = Assert.IsAssignableFrom<IDataTemplate>(items.ItemTemplate);
                var first = Assert.IsType<Border>(template.Build(NewPerson(type, "first", true)));
                var second = Assert.IsType<Border>(template.Build(NewPerson(type, "second", true)));
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                var snapshots = Assert.IsAssignableFrom<System.Collections.IList>(type.Assembly
                    .GetType("Demo.ContextProbeExtension")!.GetProperty("Snapshots")!.GetValue(null));
                Assert.Equal(2, snapshots.Count);
                var firstContext = snapshots[0]!;
                var secondContext = snapshots[1]!;
                Assert.Same(firstText, Read<object>(firstContext, "Target"));
                Assert.Same(secondText, Read<object>(secondContext, "Target"));
                Assert.Same(TextBlock.TextProperty, Read<object>(firstContext, "Property"));
                Assert.Same(owner, Read<object>(firstContext, "Root"));
                Assert.Same(first, Read<object>(firstContext, "IntermediateRoot"));
                Assert.Same(second, Read<object>(secondContext, "IntermediateRoot"));
                var firstParents = Read<object[]>(firstContext, "Parents");
                Assert.Contains(owner, firstParents);
                Assert.Contains(items, firstParents);
                Assert.Single(firstParents.OfType<Border>(), parent => ReferenceEquals(parent, first));
                Assert.Contains(firstText, firstParents);
                Assert.DoesNotContain(second, firstParents);
                Assert.Same(items.Resources["AccentBrush"], firstText.Background);
                Assert.Same(firstText.Background, secondText.Background);
                panel.Children.Add(first);
                panel.Children.Add(second);
                owner.InvalidState();
                Assert.Same(firstText, first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal(2, snapshots.Count);
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
    public async Task DeferredTemplate_InstancesKeepSeparateStorageAndFollowParentUpdates(bool structural)
    {
        var type = Compile(
            """
            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border>
                            $if (Expanded) { <TextBox Text={Text} /> }
                            $else { <Button Content={Text} /> }
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var template = Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ItemsControl>(owner.Child).ItemTemplate);
                var first = Assert.IsType<Border>(template.Build(null));
                var second = Assert.IsType<Border>(template.Build(null));
                panel.Children.Add(first);
                panel.Children.Add(second);
                var firstText = Assert.IsType<TextBox>(first.Child);
                var secondText = Assert.IsType<TextBox>(second.Child);
                Assert.NotSame(firstText, secondText);
                Set(owner, "Text", "parent changed");
                owner.InvalidState();
                Assert.Same(firstText, first.Child);
                Assert.Same(secondText, second.Child);
                Assert.Equal("parent changed", firstText.Text);
                Assert.Equal("parent changed", secondText.Text);
                Set(owner, "Expanded", false);
                owner.InvalidState();
                Assert.IsType<Button>(first.Child);
                Assert.IsType<Button>(second.Child);
                Assert.NotSame(first.Child, second.Child);
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
    public async Task TemplateInsideOuterConditional_UnmountsInstancesAndStartsFreshOnReentry(bool structural)
    {
        var type = Compile(
            """
            <Border>
                $if (Expanded)
                {
                    <StackPanel>
                        <ItemsControl>
                            <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                                <Border>
                                    $if (Evaluate(person)) { <TextBlock Text={person.Name} /> }
                                </Border>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                }
            </Border>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var componentRoot = Assert.IsType<Border>(owner.Child);
                var firstPanel = Assert.IsType<StackPanel>(componentRoot.Child);
                var firstItems = Assert.IsType<ItemsControl>(Assert.Single(firstPanel.Children));
                var firstTemplate = Assert.IsAssignableFrom<IDataTemplate>(firstItems.ItemTemplate);
                var first = Assert.IsType<Border>(firstTemplate.Build(NewPerson(type, "first", true)));
                var second = Assert.IsType<Border>(firstTemplate.Build(NewPerson(type, "second", true)));
                firstPanel.Children.Add(first);
                firstPanel.Children.Add(second);
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                Assert.NotSame(firstText, secondText);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));
                owner.InvalidState();
                Assert.Same(firstPanel, componentRoot.Child);
                Assert.Same(firstText, first.Child);
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Expanded", false);
                owner.InvalidState();
                Assert.Null(componentRoot.Child);
                Assert.Null(first.Child);
                Assert.Null(second.Child);
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));
                owner.InvalidState();
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Expanded", true);
                owner.InvalidState();
                var newPanel = Assert.IsType<StackPanel>(componentRoot.Child);
                Assert.NotSame(firstPanel, newPanel);
                var newItems = Assert.IsType<ItemsControl>(Assert.Single(newPanel.Children));
                var newTemplate = Assert.IsAssignableFrom<IDataTemplate>(newItems.ItemTemplate);
                var fresh = Assert.IsType<Border>(newTemplate.Build(NewPerson(type, "fresh", true)));
                newPanel.Children.Add(fresh);
                Assert.NotSame(firstText, fresh.Child);
                Assert.Equal("fresh", Assert.IsType<TextBlock>(fresh.Child).Text);
                Assert.Equal(5, Read<int>(owner, "ConditionEvaluations"));
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
    public async Task UnattachedTemplateInstances_StopRenderingWhenTheirConditionalFactoryOwnerExits(bool structural)
    {
        var type = Compile(
            """
            <Border>
                $if (Expanded)
                {
                    <ItemsControl>
                        <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                            <Border>
                                $if (Evaluate(person)) { <TextBlock Text={person.Name} /> }
                            </Border>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                }
            </Border>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var componentRoot = Assert.IsType<Border>(owner.Child);
                var items = Assert.IsType<ItemsControl>(componentRoot.Child);
                var template = Assert.IsAssignableFrom<IDataTemplate>(items.ItemTemplate);
                var first = Assert.IsType<Border>(template.Build(NewPerson(type, "first", true)));
                var second = Assert.IsType<Border>(template.Build(NewPerson(type, "second", true)));
                Assert.False(first.IsAttachedToVisualTree());
                Assert.False(second.IsAttachedToVisualTree());
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));
                owner.InvalidState();
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Expanded", false);
                owner.InvalidState();
                Assert.Null(componentRoot.Child);
                Assert.Null(first.Child);
                Assert.Null(second.Child);
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));
                owner.InvalidState();
                Assert.Equal(4, Read<int>(owner, "ConditionEvaluations"));
                GC.KeepAlive(first);
                GC.KeepAlive(second);
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
    public async Task NestedTypedTemplate_PreservesAncestorItemScopeWithoutShadowingState(bool structural)
    {
        var type = Compile(
            """
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Group" x.ItemName="group">
                    <ItemsControl>
                        <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                            <Border>
                                $if (person.Selected) { <TextBlock Text={group.Name + ": " + person.Name} /> }
                            </Border>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var outerTemplate = Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ItemsControl>(owner.Child).ItemTemplate);
                var group = Activator.CreateInstance(type.Assembly.GetType("Demo.Group")!)!;
                Set(group, "Name", "group");
                var nestedOwner = Assert.IsType<ItemsControl>(outerTemplate.Build(group));
                panel.Children.Add(nestedOwner);
                var innerTemplate = Assert.IsAssignableFrom<IDataTemplate>(nestedOwner.ItemTemplate);
                var person = NewPerson(type, "person", true);
                var root = Assert.IsType<Border>(innerTemplate.Build(person));
                panel.Children.Add(root);
                var text = Assert.IsType<TextBlock>(root.Child);
                Assert.Equal("group: person", text.Text);
                Set(group, "Name", "updated group");
                owner.InvalidState();
                Assert.Same(text, root.Child);
                Assert.Equal("updated group: person", text.Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Type Compile(string source, bool structural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Avalonia.Markup.Xaml.Templates; using Demo; " + source, OwnerSource);
        Assert.False(fixture.ComponentTree.GetRoot().ContainsDiagnostics);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticDiagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        Assert.True(semanticDiagnostics.IsEmpty, string.Join(Environment.NewLine,
            semanticDiagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            "Views/PlannerView.akbura", new Dictionary<AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural) options = options.WithPreprocessorSymbols("DEBUG");
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, options))
            .WithAssemblyName("ConditionalTemplates_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + generated);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private static AkburaControl Create(Type type) => Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
    private static T Read<T>(object value, string property) => (T)value.GetType().GetProperty(property)!.GetValue(value)!;
    private static void Set(object value, string property, object? target) => value.GetType().GetProperty(property)!.SetValue(value, target);
    private static object NewPerson(Type type, string name, bool selected)
    {
        var person = Activator.CreateInstance(type.Assembly.GetType("Demo.Person")!)!;
        Set(person, "Name", name);
        Set(person, "Selected", selected);
        return person;
    }

    private const string OwnerSource =
        """
        namespace Demo;
        public partial class PlannerView : Akbura.AkburaControl
        {
            public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public bool Expanded { get; set; } = true;
            public string Text { get; set; } = "initial";
            public int ConditionEvaluations { get; set; }
            public bool Evaluate(Person person) { ConditionEvaluations++; return person.Selected; }
            public bool EvaluateControl() { ConditionEvaluations++; return Expanded; }
            public bool AlternateRoot { get; set; }
            public Avalonia.Controls.Control? SuppliedRoot { get; set; }
            public string BranchName { get; set; } = "branchSource";
            public object? Model { get; set; }
            public bool CanUsePerson { get; set; } = true;
            public bool TryPerson(out Person person)
            {
                person = Model as Person ?? new Person { Name = "missing" };
                return Model is Person && CanUsePerson;
            }
            public bool TryOptionalPerson([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Person? person)
            {
                person = Model as Person;
                return person != null && CanUsePerson;
            }
        }
        public sealed class Person
        {
            public string Name { get; set; } = "person";
            public bool Selected { get; set; }
            public int Mode { get; set; }
        }
        public sealed class Group { public string Name { get; set; } = "group"; }
        public sealed class CountingTextBlock : Avalonia.Controls.TextBlock
        {
            public static int Created { get; private set; }
            public CountingTextBlock() { Created++; }
        }
        public sealed class NameButton : Avalonia.Controls.Button
        {
            public Avalonia.Controls.Control? AppliedPart { get; private set; }
            protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
            {
                base.OnApplyTemplate(e);
                AppliedPart = e.NameScope.Find("PART_Source") as Avalonia.Controls.Control;
            }
        }
        public sealed class ContextProbeExtension
        {
            public static System.Collections.Generic.List<ContextSnapshot> Snapshots { get; } = new();
            public string ProvideValue(System.IServiceProvider services)
            {
                var target = (Avalonia.Markup.Xaml.IProvideValueTarget)services.GetService(
                    typeof(Avalonia.Markup.Xaml.IProvideValueTarget))!;
                var root = (Avalonia.Markup.Xaml.IRootObjectProvider)services.GetService(
                    typeof(Avalonia.Markup.Xaml.IRootObjectProvider))!;
                var parents = (Avalonia.Markup.Xaml.XamlIl.Runtime.IAvaloniaXamlIlParentStackProvider)
                    services.GetService(typeof(Avalonia.Markup.Xaml.XamlIl.Runtime.IAvaloniaXamlIlParentStackProvider))!;
                Snapshots.Add(new ContextSnapshot(target.TargetObject!, target.TargetProperty!, root.RootObject!,
                    root.IntermediateRootObject!, System.Linq.Enumerable.ToArray(parents.Parents)));
                return "context";
            }
        }
        public sealed class ContextSnapshot
        {
            public ContextSnapshot(object target, object property, object root, object intermediateRoot, object[] parents)
            {
                Target = target; Property = property; Root = root; IntermediateRoot = intermediateRoot; Parents = parents;
            }
            public object Target { get; }
            public object Property { get; }
            public object Root { get; }
            public object IntermediateRoot { get; }
            public object[] Parents { get; }
        }
        """;
}
