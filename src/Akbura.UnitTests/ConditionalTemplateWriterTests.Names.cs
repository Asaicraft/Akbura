using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace Akbura.UnitTests;

public sealed partial class ConditionalTemplateWriterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConditionalTemplateRoot_NonmatchingDataUnmountsTheOldInstanceWithoutChangingNativeFallback(
        bool deferred, bool structural)
    {
        var body =
            """
            $if (person != null && Evaluate(person))
            {
                <StackPanel>
                    <TextBox x.Name="itemSource" Text={person.Name} />
                    <TextBlock Text=${Binding #itemSource.Text} />
                </StackPanel>
            }
            """;
        var template = deferred
            ? "<ContentPresenter.ContentTemplate><DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
                body + "</DataTemplate></ContentPresenter.ContentTemplate>"
            : "<ContentPresenter.ContentTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
                body + "</ContentPresenter.ContentTemplate>";
        var type = Compile("using Avalonia.Controls.Presenters; <ContentPresenter Content={Model}>" +
            template + "</ContentPresenter>", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var person = NewPerson(type, "first", true);
            Set(owner, "Model", person);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var host = Assert.IsType<ContentPresenter>(owner.Child);
                var descriptor = Assert.IsAssignableFrom<IDataTemplate>(host.ContentTemplate);
                var firstRoot = Assert.IsType<StackPanel>(host.Child);
                Assert.Equal(2, firstRoot.Children.Count);
                var firstSource = Assert.IsType<TextBox>(firstRoot.Children[0]);
                var firstTarget = Assert.IsType<TextBlock>(firstRoot.Children[1]);
                Assert.Equal("first", firstTarget.Text);
                Assert.Equal(1, Read<int>(owner, "ConditionEvaluations"));

                Set(owner, "Model", "native fallback");
                owner.InvalidState();
                var fallback = Assert.IsType<TextBlock>(host.Child);
                Assert.Equal("native fallback", fallback.Text);
                Assert.Same(descriptor, host.ContentTemplate);
                Assert.Null(firstRoot.GetVisualParent());
                Assert.Equal(1, Read<int>(owner, "ConditionEvaluations"));
                var exitedText = firstTarget.Text;
                firstSource.Text = "inactive source";
                Assert.Equal(exitedText, firstTarget.Text);
                owner.InvalidState();
                Assert.Same(fallback, host.Child);
                Assert.Equal("native fallback", fallback.Text);
                Assert.Equal(1, Read<int>(owner, "ConditionEvaluations"));

                Set(person, "Name", "returned");
                Set(owner, "Model", person);
                owner.InvalidState();
                var returnedRoot = Assert.IsType<StackPanel>(host.Child);
                Assert.NotSame(firstRoot, returnedRoot);
                Assert.Equal(2, returnedRoot.Children.Count);
                var returnedSource = Assert.IsType<TextBox>(returnedRoot.Children[0]);
                var returnedTarget = Assert.IsType<TextBlock>(returnedRoot.Children[1]);
                Assert.NotSame(firstTarget, returnedTarget);
                Assert.Equal("returned", returnedTarget.Text);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));
                returnedSource.Text = "returned live source";
                Assert.Equal("returned live source", returnedTarget.Text);
                firstSource.Text = "still inactive";
                Assert.Equal(exitedText, firstTarget.Text);
                owner.InvalidState();
                Assert.Same(returnedRoot, host.Child);
                Assert.Same(returnedTarget, returnedRoot.Children[1]);
                Assert.Equal("returned", returnedTarget.Text);
                Assert.Equal(3, Read<int>(owner, "ConditionEvaluations"));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ComponentContentPresenter_RetainsItsLiveTemplateForTheSameItemAndRebuildsForANewItem(
        bool deferred, bool structural, bool explicitContent)
    {
        var source =
            """
            <ContentPresenter Content={Model}>
                <ContentPresenter.ContentTemplate x.DataType="Person" x.ItemName="person">
                    <Border>
                        $if (person is { Selected: true })
                        {
                            <StackPanel>
                                <TextBox x.Name="itemSource" Text={person.Name} />
                                <TextBlock Text=${Binding #itemSource.Text} />
                            </StackPanel>
                        }
                    </Border>
                </ContentPresenter.ContentTemplate>
            </ContentPresenter>
            """;
        if (deferred)
        {
            source = source.Replace(
                "<ContentPresenter.ContentTemplate x.DataType=\"Person\" x.ItemName=\"person\">",
                "<ContentPresenter.ContentTemplate>")
                .Replace("<Border>", "<DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">")
                .Replace("</Border>", "</DataTemplate>");
        }
        if (explicitContent)
        {
            source = source.Replace("<DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">",
                "<DataTemplate x.DataType=\"Person\"><DataTemplate.Content x.DataType=\"Person\" x.ItemName=\"person\">")
                .Replace("</DataTemplate>", "</DataTemplate.Content></DataTemplate>");
        }

        var type = Compile("using Avalonia.Controls.Presenters; " + source, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var firstItem = NewPerson(type, "first", true);
            Set(owner, "Model", firstItem);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var host = Assert.IsType<ContentPresenter>(owner.Child);
                var template = Assert.IsAssignableFrom<IDataTemplate>(host.ContentTemplate);
                var firstRoot = Assert.IsAssignableFrom<Control>(host.Child);
                var firstBranch = deferred ? Assert.IsType<StackPanel>(firstRoot) :
                    Assert.IsType<StackPanel>(Assert.IsType<Border>(firstRoot).Child);
                Assert.Equal(2, firstBranch.Children.Count);
                var firstSource = Assert.IsType<TextBox>(firstBranch.Children[0]);
                var firstTarget = Assert.IsType<TextBlock>(firstBranch.Children[1]);
                Assert.Equal("first", firstTarget.Text);

                Set(firstItem, "Name", "first current");
                owner.InvalidState();
                Assert.Same(firstItem, host.Content);
                Assert.Same(template, host.ContentTemplate);
                Assert.Same(firstRoot, host.Child);
                Assert.Same(firstTarget, firstBranch.Children[1]);
                Assert.Equal("first current", firstTarget.Text);
                firstSource.Text = "live observer";
                Assert.Equal("live observer", firstTarget.Text);
                owner.InvalidState();
                Assert.Same(firstRoot, host.Child);
                Assert.Same(firstTarget, firstBranch.Children[1]);
                Assert.Equal("first current", firstTarget.Text);

                var secondItem = NewPerson(type, "second", true);
                Set(owner, "Model", secondItem);
                owner.InvalidState();
                Assert.Same(secondItem, host.Content);
                Assert.Same(template, host.ContentTemplate);
                var secondRoot = Assert.IsAssignableFrom<Control>(host.Child);
                Assert.NotSame(firstRoot, secondRoot);
                var secondBranch = deferred ? Assert.IsType<StackPanel>(secondRoot) :
                    Assert.IsType<StackPanel>(Assert.IsType<Border>(secondRoot).Child);
                Assert.Equal(2, secondBranch.Children.Count);
                var secondSource = Assert.IsType<TextBox>(secondBranch.Children[0]);
                var secondTarget = Assert.IsType<TextBlock>(secondBranch.Children[1]);
                Assert.NotSame(firstTarget, secondTarget);
                Assert.Equal("second", secondTarget.Text);
                var exitedText = firstTarget.Text;
                firstSource.Text = "inactive observer";
                Assert.Equal(exitedText, firstTarget.Text);
                secondSource.Text = "new live observer";
                Assert.Equal("new live observer", secondTarget.Text);

                Set(secondItem, "Name", "second current");
                owner.InvalidState();
                Assert.Same(secondRoot, host.Child);
                Assert.Same(secondTarget, secondBranch.Children[1]);
                Assert.Equal("second current", secondTarget.Text);
                if (deferred)
                {
                    window.Close();
                    Assert.Same(template, host.ContentTemplate);
                    Assert.Null(host.Child);
                    Assert.Null(firstRoot.GetVisualParent());
                    Assert.Null(secondRoot.GetVisualParent());
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task ConditionalTemplate_UsesUnconditionalComponentNamesAcrossStorageRoots(bool wholeRoot, bool deferred, bool structural)
    {
        return VerifyUnconditionalComponentNamesAcrossStorageRoots(wholeRoot, deferred, structural, literalName: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConditionalTemplate_LiteralElementNameUsesTheActualAncestorAcrossStorageRoots(bool structural)
    {
        return VerifyUnconditionalComponentNamesAcrossStorageRoots(wholeRoot: false, deferred: false, structural, literalName: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OrdinaryTemplate_LiteralElementNameUsesTheComponentStorageWithoutANativeRegistry(bool structural)
    {
        return VerifyUnconditionalComponentNamesAcrossStorageRoots(wholeRoot: false, deferred: false, structural,
            literalName: true, noIf: true);
    }

    [Fact]
    public Task OrdinaryStructuralTemplate_DynamicElementNameRequiresOnlyTheComponentNativeLookupRegistry()
    {
        return VerifyUnconditionalComponentNamesAcrossStorageRoots(wholeRoot: false, deferred: false, structural: true,
            literalName: false, noIf: true);
    }

    [Fact]
    public async Task OrdinaryStructuralTemplate_PreservesItsLegalInlineAnonymousCapture()
    {
        var source =
            """
            using Avalonia.Data;
            var names = new { Name = BranchName };
            <StackPanel>
                <TextBox x.Name="outer" />
                <ItemsControl>
                    <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                        <Border>
                            <TextBlock Text=${ReflectionBinding Path=Text, ElementName={names.Name}} />
                        </Border>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            """;
        var type = Compile(source, structural: true);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                Assert.Equal("outer", Assert.IsType<TextBox>(root.Children[0]).Name);
                Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ItemsControl>(root.Children[1]).ItemTemplate);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private async Task VerifyUnconditionalComponentNamesAcrossStorageRoots(bool wholeRoot, bool deferred, bool structural,
        bool literalName, bool noIf = false)
    {
        var source =
            """
            using Avalonia.Data;
            <StackPanel>
                <TextBox x.Name="outer" Text="initial" />
                <ItemsControl>
                    <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                        <Border>
                            $if (person != null && person.Selected)
                            {
                                <TextBlock Text=${ReflectionBinding Path=Text, ElementName={BranchName}} />
                            }
                        </Border>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            """;
        if (noIf)
        {
            source =
                """
                using Avalonia.Data;
                <StackPanel>
                    <TextBox x.Name="outer" Text="initial" />
                    <ItemsControl>
                        <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                            <Border>
                                <TextBlock Text=${ReflectionBinding Path=Text, ElementName={BranchName}} />
                            </Border>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
                """;
        }
        if (literalName)
        {
            source = source.Replace("ElementName={BranchName}", "ElementName=\"outer\"");
        }
        else if (noIf)
        {
            source = "string prefix = BranchName;\r\n" + source.Replace("ElementName={BranchName}", "ElementName={prefix}");
        }
        if (wholeRoot)
        {
            source = source.Replace("<Border>", string.Empty).Replace("</Border>", string.Empty);
        }
        if (deferred)
        {
            source = source.Replace("<ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">",
                "<ItemsControl.ItemTemplate>\r\n                <DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">")
                .Replace("</ItemsControl.ItemTemplate>", "</DataTemplate>\r\n                </ItemsControl.ItemTemplate>");
            if (!wholeRoot)
            {
                source = source.Replace("person != null && person.Selected", "Expanded");
            }
        }

        var type = Compile(source, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var firstOwner = Create(type);
            var secondOwner = Create(type);
            Set(firstOwner, "BranchName", "outer");
            Set(secondOwner, "BranchName", "outer");
            var panel = new StackPanel();
            panel.Children.Add(firstOwner);
            panel.Children.Add(secondOwner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var firstComponent = Assert.IsType<StackPanel>(firstOwner.Child);
                var secondComponent = Assert.IsType<StackPanel>(secondOwner.Child);
                var firstSource = Assert.IsType<TextBox>(firstComponent.Children[0]);
                var secondSource = Assert.IsType<TextBox>(secondComponent.Children[0]);
                firstSource.Text = "first component";
                secondSource.Text = "second component";
                var firstTemplate = Assert.IsAssignableFrom<IDataTemplate>(
                    Assert.IsType<ItemsControl>(firstComponent.Children[1]).ItemTemplate);
                var secondTemplate = Assert.IsAssignableFrom<IDataTemplate>(
                    Assert.IsType<ItemsControl>(secondComponent.Children[1]).ItemTemplate);
                var person = NewPerson(type, "same item", true);
                var firstHost = new ContentPresenter { Content = person, ContentTemplate = firstTemplate };
                var secondHost = new ContentPresenter { Content = person, ContentTemplate = secondTemplate };
                panel.Children.Add(firstHost);
                panel.Children.Add(secondHost);
                firstHost.UpdateChild();
                secondHost.UpdateChild();

                TextBlock Target(ContentPresenter host) => wholeRoot
                    ? Assert.IsType<TextBlock>(host.Child)
                    : Assert.IsType<TextBlock>(Assert.IsType<Border>(host.Child).Child);
                var firstTarget = Target(firstHost);
                var secondTarget = Target(secondHost);
                Assert.Equal("first component", firstTarget.Text);
                Assert.Equal("second component", secondTarget.Text);
                if (!noIf || !literalName)
                {
                    Assert.Same(firstSource, firstTarget.FindControl<TextBox>("outer"));
                    Assert.Same(secondSource, secondTarget.FindControl<TextBox>("outer"));
                }
                firstSource.Text = "first current";
                Assert.Equal("first current", firstTarget.Text);
                Assert.Equal("second component", secondTarget.Text);
                firstOwner.InvalidState();
                Assert.Same(firstSource, firstComponent.Children[0]);
                Assert.Same(firstTarget, Target(firstHost));
                Assert.Equal("first current", firstTarget.Text);
                if (noIf)
                {
                    return;
                }

                Set(person, "Selected", false);
                Set(firstOwner, "Expanded", false);
                Set(secondOwner, "Expanded", false);
                firstOwner.InvalidState();
                secondOwner.InvalidState();
                Assert.Null(wholeRoot ? firstHost.Child : Assert.IsType<Border>(firstHost.Child).Child);
                Assert.Null(wholeRoot ? secondHost.Child : Assert.IsType<Border>(secondHost.Child).Child);
                var firstExited = firstTarget.Text;
                var secondExited = secondTarget.Text;
                firstSource.Text = "first after exit";
                secondSource.Text = "second after exit";
                Assert.Equal(firstExited, firstTarget.Text);
                Assert.Equal(secondExited, secondTarget.Text);

                Set(person, "Selected", true);
                Set(firstOwner, "Expanded", true);
                Set(secondOwner, "Expanded", true);
                firstOwner.InvalidState();
                secondOwner.InvalidState();
                var firstReentered = Target(firstHost);
                var secondReentered = Target(secondHost);
                Assert.NotSame(firstTarget, firstReentered);
                Assert.NotSame(secondTarget, secondReentered);
                Assert.Equal("first after exit", firstReentered.Text);
                Assert.Equal("second after exit", secondReentered.Text);
                Assert.Same(firstSource, firstReentered.FindControl<TextBox>("outer"));
                Assert.Same(secondSource, secondReentered.FindControl<TextBox>("outer"));
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
    public async Task ConditionalTemplate_BranchNamesSupportNativeBindingsAndLocalLookup(bool dynamicName, bool structural)
    {
        var source =
            """
            using Avalonia.Data;
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                    <Border>
                        $if (Expanded)
                        {
                            <StackPanel>
                                <TextBox x.Name="branchSource" Text={Text} />
                                <TextBlock Text=${Binding #branchSource.Text} />
                            </StackPanel>
                        }
                        $else
                        {
                            <StackPanel>
                                <TextBox x.Name="branchSource" Text="alternative" />
                                <TextBlock Text=${Binding #branchSource.Text} />
                            </StackPanel>
                        }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        if (dynamicName)
        {
            source = source.Replace("${Binding #branchSource.Text}",
                "${ReflectionBinding Path=Text, ElementName={BranchName}}");
        }

        var type = Compile(source, structural);
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
                var destination = Assert.IsType<ItemsControl>(owner.Child);
                var template = Assert.IsAssignableFrom<IDataTemplate>(destination.ItemTemplate);
                var host = new ContentPresenter
                {
                    Content = NewPerson(type, "item", true),
                    ContentTemplate = template,
                };
                panel.Children.Add(host);
                host.UpdateChild();
                var root = Assert.IsType<Border>(host.Child);
                var firstBranch = Assert.IsType<StackPanel>(root.Child);
                var firstSource = Assert.IsType<TextBox>(firstBranch.Children[0]);
                var firstTarget = Assert.IsType<TextBlock>(firstBranch.Children[1]);
                Assert.Equal("initial", firstTarget.Text);
                Assert.Same(firstSource, firstBranch.FindControl<TextBox>("branchSource"));
                Assert.Null(root.FindControl<TextBox>("branchSource"));
                firstSource.Text = "bound first";
                Assert.Equal("bound first", firstTarget.Text);

                Set(owner, "Expanded", false);
                owner.InvalidState();
                var secondBranch = Assert.IsType<StackPanel>(root.Child);
                var secondSource = Assert.IsType<TextBox>(secondBranch.Children[0]);
                var secondTarget = Assert.IsType<TextBlock>(secondBranch.Children[1]);
                Assert.NotSame(firstBranch, secondBranch);
                Assert.Equal("alternative", secondTarget.Text);
                Assert.Same(secondSource, secondBranch.FindControl<TextBox>("branchSource"));
                Assert.Null(root.FindControl<TextBox>("branchSource"));
                firstSource.Text = "inactive source";
                Assert.Equal("alternative", secondTarget.Text);
                secondSource.Text = "bound second";
                Assert.Equal("bound second", secondTarget.Text);

                Set(owner, "Expanded", true);
                owner.InvalidState();
                var remountedBranch = Assert.IsType<StackPanel>(root.Child);
                var remountedSource = Assert.IsType<TextBox>(remountedBranch.Children[0]);
                Assert.NotSame(firstSource, remountedSource);
                Assert.Same(remountedSource, remountedBranch.FindControl<TextBox>("branchSource"));
                Assert.Null(root.FindControl<TextBox>("branchSource"));
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
    public async Task ConditionalTemplate_NestedNativeScopesRetainVisibleAncestorNamesWithoutLeakingChildNames(bool structural)
    {
        var type = Compile(
            """
            using Avalonia.Data;
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                    <Border>
                        <StackPanel>
                            <TextBox x.Name="ancestorSource" Text="ancestor" />
                            $if (Expanded)
                            {
                                <Border>
                                    <StackPanel>
                                        <TextBox x.Name="branchSource" Text="outer branch" />
                                        $if (person.Selected)
                                        {
                                            <StackPanel>
                                                <TextBox x.Name="innerSource" Text="inner" />
                                                <TextBlock Text=${ReflectionBinding Path=Text, ElementName="ancestorSource"} />
                                                <TextBlock Text=${ReflectionBinding Path=Text, ElementName="branchSource"} />
                                            </StackPanel>
                                        }
                                    </StackPanel>
                                </Border>
                            }
                        </StackPanel>
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
                var person = NewPerson(type, "item", true);
                var host = new ContentPresenter { Content = person, ContentTemplate = template };
                panel.Children.Add(host);
                host.UpdateChild();
                var root = Assert.IsType<Border>(host.Child);
                var content = Assert.IsType<StackPanel>(root.Child);
                var ancestorSource = Assert.IsType<TextBox>(content.Children[0]);
                var branchRoot = Assert.IsType<Border>(content.Children[1]);
                var outer = Assert.IsType<StackPanel>(branchRoot.Child);
                var outerSource = Assert.IsType<TextBox>(outer.Children[0]);
                var inner = Assert.IsType<StackPanel>(outer.Children[1]);
                Assert.Equal("ancestor", Assert.IsType<TextBlock>(inner.Children[1]).Text);
                Assert.Equal("outer branch", Assert.IsType<TextBlock>(inner.Children[2]).Text);
                Assert.Same(ancestorSource, inner.FindControl<TextBox>("ancestorSource"));
                Assert.Same(outerSource, inner.FindControl<TextBox>("branchSource"));
                Assert.Null(branchRoot.FindControl<TextBox>("innerSource"));
                Assert.Null(root.FindControl<TextBox>("branchSource"));
                ancestorSource.Text = "ancestor current";
                outerSource.Text = "outer current";
                Assert.Equal("ancestor current", Assert.IsType<TextBlock>(inner.Children[1]).Text);
                Assert.Equal("outer current", Assert.IsType<TextBlock>(inner.Children[2]).Text);
                Set(person, "Selected", false);
                owner.InvalidState();
                Assert.Single(outer.Children);
                Assert.Null(branchRoot.FindControl<TextBox>("innerSource"));
                Set(person, "Selected", true);
                owner.InvalidState();
                var remounted = Assert.IsType<StackPanel>(outer.Children[1]);
                Assert.NotSame(inner, remounted);
                Assert.Equal("ancestor current", Assert.IsType<TextBlock>(remounted.Children[1]).Text);
                Assert.Equal("outer current", Assert.IsType<TextBlock>(remounted.Children[2]).Text);
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
    public async Task ConditionalTemplateRoot_NativeDataHostPreservesAttachedBranchNameScope(bool structural)
    {
        var type = Compile(
            """
            using Avalonia.Data;
            <ContentControl>
                <ContentControl.ContentTemplate x.DataType="Person" x.ItemName="person">
                    $if (person is { Selected: true })
                    {
                        <StackPanel>
                            <TextBox x.Name="branchSource" Text="first" />
                            <TextBlock Text=${ReflectionBinding Path=Text, ElementName="branchSource"} />
                        </StackPanel>
                    }
                    $else
                    {
                        <StackPanel>
                            <TextBox x.Name="branchSource" Text="second" />
                            <TextBlock Text=${ReflectionBinding Path=Text, ElementName="branchSource"} />
                        </StackPanel>
                    }
                </ContentControl.ContentTemplate>
            </ContentControl>
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
                var template = Assert.IsAssignableFrom<IDataTemplate>(Assert.IsType<ContentControl>(owner.Child).ContentTemplate);
                var person = NewPerson(type, "item", true);
                var host = new ContentPresenter { Content = person, ContentTemplate = template };
                panel.Children.Add(host);
                host.UpdateChild();
                var first = Assert.IsType<StackPanel>(host.Child);
                var source = Assert.IsType<TextBox>(first.Children[0]);
                var firstScope = Assert.IsAssignableFrom<INameScope>(NameScope.GetNameScope(first));
                Assert.Same(source, firstScope.Find("branchSource"));
                Assert.Same(source, first.FindControl<TextBox>("branchSource"));
                Assert.Equal("first", Assert.IsType<TextBlock>(first.Children[1]).Text);
                Set(person, "Selected", false);
                owner.InvalidState();
                var second = Assert.IsType<StackPanel>(host.Child);
                Assert.NotSame(first, second);
                Assert.NotSame(firstScope, NameScope.GetNameScope(second));
                Assert.Same(second.Children[0], second.FindControl<TextBox>("branchSource"));
                Assert.Equal("second", Assert.IsType<TextBlock>(second.Children[1]).Text);
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
    public async Task ConditionalControlTemplateRoot_NativeTemplateAppliedReceivesTheSelectedPartScope(bool structural)
    {
        var type = Compile(
            """
            <NameButton>
                <NameButton.Template>
                    <ControlTemplate>
                        $if (Expanded) { <TextBox x.Name="PART_Source" Text="first" /> }
                        $else { <TextBlock x.Name="PART_Source" Text="second" /> }
                    </ControlTemplate>
                </NameButton.Template>
            </NameButton>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var button = Assert.IsAssignableFrom<Button>(owner.Child);
                button.ApplyTemplate();
                var first = Assert.IsType<TextBox>(button.GetVisualChildren().Single());
                Assert.Same(first, Read<Control?>(button, "AppliedPart"));
                Assert.Same(first, first.FindControl<TextBox>("PART_Source"));
                Set(owner, "Expanded", false);
                owner.InvalidState();
                button.ApplyTemplate();
                var second = Assert.IsType<TextBlock>(button.GetVisualChildren().Single());
                Assert.Same(second, Read<Control?>(button, "AppliedPart"));
                Assert.Same(second, second.FindControl<TextBlock>("PART_Source"));
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
    public async Task NestedConditionalTemplate_UsesCurrentAncestorInstanceNamesAndRespectsInnerShadowing(bool shadow, bool structural)
    {
        var source =
            """
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Group" x.ItemName="group">
                    <Border>
                        $if (Expanded)
                        {
                            <StackPanel>
                                <TextBox x.Name="outerSource" Text={group.Name} />
                                <ItemsControl>
                                    <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                                        <Border>
                                            $if (person.Selected)
                                            {
                                                <StackPanel>
                                                    <TextBlock Text=${Binding #outerSource.Text} />
                                                </StackPanel>
                                            }
                                        </Border>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        if (shadow)
        {
            source = source.Replace("<TextBlock Text=${Binding #outerSource.Text} />",
                "<TextBox x.Name=\"outerSource\" Text=\"inner scoped\" />\r\n" +
                "                                                    <TextBlock Text=${Binding #outerSource.Text} />");
        }
        var type = Compile(source, structural);
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
                Set(firstGroup, "Name", "first outer");
                Set(secondGroup, "Name", "second outer");
                var firstHost = new ContentPresenter { Content = firstGroup, ContentTemplate = template };
                var secondHost = new ContentPresenter { Content = secondGroup, ContentTemplate = template };
                panel.Children.Add(firstHost);
                panel.Children.Add(secondHost);
                firstHost.UpdateChild();
                secondHost.UpdateChild();
                var firstOuter = Assert.IsType<StackPanel>(Assert.IsType<Border>(firstHost.Child).Child);
                var secondOuter = Assert.IsType<StackPanel>(Assert.IsType<Border>(secondHost.Child).Child);
                var firstSource = Assert.IsType<TextBox>(firstOuter.Children[0]);
                var secondSource = Assert.IsType<TextBox>(secondOuter.Children[0]);
                var firstOwner = Assert.IsType<ItemsControl>(firstOuter.Children[1]);
                var secondOwner = Assert.IsType<ItemsControl>(secondOuter.Children[1]);
                var firstTemplate = Assert.IsAssignableFrom<IDataTemplate>(firstOwner.ItemTemplate);
                var secondTemplate = Assert.IsAssignableFrom<IDataTemplate>(secondOwner.ItemTemplate);
                var person = NewPerson(type, "same item", true);
                var firstInnerHost = new ContentPresenter { Content = person, ContentTemplate = firstTemplate };
                var secondInnerHost = new ContentPresenter { Content = person, ContentTemplate = secondTemplate };
                panel.Children.Add(firstInnerHost);
                panel.Children.Add(secondInnerHost);
                firstInnerHost.UpdateChild();
                secondInnerHost.UpdateChild();
                var firstInner = Assert.IsType<StackPanel>(Assert.IsType<Border>(firstInnerHost.Child).Child);
                var secondInner = Assert.IsType<StackPanel>(Assert.IsType<Border>(secondInnerHost.Child).Child);
                Assert.Equal(shadow ? 2 : 1, firstInner.Children.Count);
                Assert.Equal(shadow ? 2 : 1, secondInner.Children.Count);
                var firstTarget = Assert.Single(firstInner.Children.OfType<TextBlock>());
                var secondTarget = Assert.Single(secondInner.Children.OfType<TextBlock>());
                Assert.Equal(shadow ? "inner scoped" : "first outer", firstTarget.Text);
                Assert.Equal(shadow ? "inner scoped" : "second outer", secondTarget.Text);
                Assert.Same(shadow ? firstInner.Children[0] : firstSource, firstInner.FindControl<TextBox>("outerSource"));
                Assert.Same(shadow ? secondInner.Children[0] : secondSource, secondInner.FindControl<TextBox>("outerSource"));
                firstSource.Text = "first current";
                Assert.Equal(shadow ? "inner scoped" : "first current", firstTarget.Text);
                Assert.Equal(shadow ? "inner scoped" : "second outer", secondTarget.Text);
                if (shadow)
                {
                    Assert.IsType<TextBox>(firstInner.Children[0]).Text = "inner current";
                    Assert.Equal("inner current", firstTarget.Text);
                    Assert.Equal("inner scoped", secondTarget.Text);
                }
                owner.InvalidState();
                Assert.Same(firstTarget, Assert.Single(Assert.IsType<StackPanel>(Assert.IsType<Border>(firstInnerHost.Child).Child)
                    .Children.OfType<TextBlock>()));
                Set(owner, "Expanded", false);
                owner.InvalidState();
                var exited = firstTarget.Text;
                firstSource.Text = "inactive outer";
                Assert.Equal(exited, firstTarget.Text);
                GC.KeepAlive(firstInnerHost);
                GC.KeepAlive(secondInnerHost);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
