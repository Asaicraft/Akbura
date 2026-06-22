using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public sealed class GeneratedDashboardProjectSyntaxTests
{
    [Fact]
    public void GeneratedDashboardProject_AkburaFiles_ParseAndRoundTrip()
    {
        AssertAkburaFileRoundTrips(AppAkbura, expectedTopLevelMembers: 4);
        AssertAkburaFileRoundTrips(DashboardPageAkbura, expectedTopLevelMembers: 14);
        AssertAkburaFileRoundTrips(CounterAkbura, expectedTopLevelMembers: 8);
        AssertAkburaFileRoundTrips(TodoListAkbura, expectedTopLevelMembers: 11);
        AssertAkburaFileRoundTrips(UserProfileAkbura, expectedTopLevelMembers: 8);
        AssertAkburaFileRoundTrips(CustomButtonAkbura, expectedTopLevelMembers: 7);
    }

    [Fact]
    public void GeneratedDashboardProject_AkcssFiles_ParseAndRoundTrip()
    {
        var shared = AssertAkcssFileRoundTrips(SharedAkcss);
        Assert.Equal(1, shared.Members.Count);
        var utilities = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(shared.Members[0]);
        Assert.Equal(6, utilities.Utilities.Count);

        var dashboard = AssertAkcssFileRoundTrips(DashboardPageAkcss);
        Assert.Equal(6, dashboard.Members.Count);
        Assert.IsType<GreenAkcssUsingDirectiveSyntax>(dashboard.Members[0]);
        Assert.IsType<GreenAkcssUsingDirectiveSyntax>(dashboard.Members[1]);
        for (var index = 2; index < dashboard.Members.Count; index++)
        {
            Assert.IsType<GreenAkcssStyleRuleSyntax>(dashboard.Members[index]);
        }
    }

    [Fact]
    public void GeneratedDashboardProject_DashboardConditionalBranches_StayTopLevel()
    {
        var syntax = AssertAkburaFileRoundTrips(DashboardPageAkbura, expectedTopLevelMembers: 14);
        var conditionals = new List<GreenCSharpStatementSyntax>();
        for (var index = 0; index < syntax.Members.Count; index++)
        {
            if (syntax.Members[index] is GreenCSharpStatementSyntax conditional)
            {
                conditionals.Add(conditional);
            }
        }

        Assert.Equal(3, conditionals.Count);
        Assert.All(conditionals, conditional =>
        {
            Assert.NotNull(conditional.Body);
            Assert.Equal(1, conditional.Body!.Tokens.Count);
            Assert.IsType<GreenMarkupRootSyntax>(conditional.Body.Tokens[0]);
        });

        Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[^1]);
    }

    private static GreenAkburaDocumentSyntax AssertAkburaFileRoundTrips(
        string code,
        int expectedTopLevelMembers)
    {
        var parser = MakeParser(code);
        var syntax = parser.ParseCompilationUnit();

        Assert.Equal(code.Length, syntax.FullWidth);
        Assert.Equal(code, syntax.ToFullString());
        Assert.Equal(expectedTopLevelMembers, syntax.Members.Count);
        return syntax;
    }

    private static GreenAkcssDocumentSyntax AssertAkcssFileRoundTrips(string code)
    {
        var parser = MakeParser(code);
        var syntax = parser.ParseAkcssDocumentSyntax();

        Assert.Equal(code.Length, syntax.FullWidth);
        Assert.Equal(code, syntax.ToFullString());
        return syntax;
    }

    private const string AppAkbura =
        """
        using Avalonia.Controls;
        using Demo.Pages;

        namespace Demo;

        <DashboardPage />
        """;

    private const string DashboardPageAkbura =
        """
        using System;
        using Avalonia.Controls;
        using Demo.Styles.Shared.akcss;
        using Demo.Components;

        namespace Demo.Pages;

        inject ILogger<DashboardPage> logger;

        state string currentTab = "counter";
        state bool sidebarOpen = true;

        @akcss {
            .sidebar {
                Background: "#F0F0F0";
                Width: 200;
            }

            .tabBtn {
                Margin: (vertical: 4);
            }
        }

        useEffect(currentTab, sidebarOpen) {
            logger.LogInformation("Dashboard state changed: Tab={0}, Sidebar={1}", currentTab, sidebarOpen);
        }

        if (currentTab == "counter") {
            <Counter InitialValue={10} />
        }

        if (currentTab == "todo") {
            <TodoList />
        }

        if (currentTab == "profile") {
            <UserProfile User={Demo.Models.User.Default} />
        }

        <DockPanel>
            <Border class="sidebar" IsVisible={sidebarOpen}>
                <StackPanel>
                    <Button class="tabBtn primary" Click={currentTab = "counter"}>Counter</Button>
                    <Button class="tabBtn primary" Click={currentTab = "todo"}>Todo</Button>
                    <Button class="tabBtn primary" Click={currentTab = "profile"}>Profile</Button>
                </StackPanel>
            </Border>

            <ContentControl />
        </DockPanel>
        """;

    private const string CounterAkbura =
        """
        using System;
        using Avalonia.Controls;

        namespace Demo.Pages;

        param int InitialValue = 0;
        param bind int Value = 0;

        state int count = bind Value;

        useEffect(count) {
            Console.WriteLine($"Counter changed to {count}");
        }

        <StackPanel class="card" w-30>
            <TextBlock Text={$"Count: {count}"} />
            <StackPanel Orientation="Horizontal" Spacing={4}>
                <Button Click={count--}>-</Button>
                <Button Click={count++}>+</Button>
            </StackPanel>
            <TextBlock Text={$"Initial was {InitialValue}"} />
        </StackPanel>
        """;

    private const string TodoListAkbura =
        """
        using System.Collections.Generic;
        using Avalonia.Controls;
        using Avalonia.Interactivity;

        namespace Demo.Pages;

        state List<string> items = new List<string>();
        state string newItem = "";

        command void AddItem(string text);
        command void RemoveItem(int index);

        useEffect(AddItem.IsExecuting) {
            if (string.IsNullOrWhiteSpace(newItem)) return;
            items.Add(newItem);
            newItem = "";
        }

        useEffect(RemoveItem.IsExecuting) {
            logger.LogInformation("Remove command executing");
        }

        <StackPanel>
            <StackPanel Orientation="Horizontal">
                <TextBox bind:Text={newItem} Watermark="New task" />
                <Button Click={AddItem.Execute(newItem)}>Add</Button>
            </StackPanel>
            <ItemsControl ItemsSource={items}>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Text={$"{index + 1}. {item}"} />
                            <Button Click={RemoveItem.Execute(index)}>X</Button>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
        """;

    private const string UserProfileAkbura =
        """
        using Avalonia.Controls;
        using Demo.Models;

        namespace Demo.Pages;

        param UserModel User;

        state string name = bind User.Name;
        state int age = bind User.Age;
        state string email = bind User.Email;

        <StackPanel>
            <TextBlock Text="Edit Profile" FontWeight="Bold" />
            <TextBox bind:Text={name} Watermark="Name" />
            <NumericUpDown bind:Value={age} Minimum={0} Maximum={120} />
            <TextBox bind:Text={email} Watermark="Email" />
            <Button primary>Save</Button>
        </StackPanel>
        """;

    private const string CustomButtonAkbura =
        """
        using System;
        using Avalonia.Controls;

        namespace Demo.Components;

        command int Click(int value);

        state int clicked = 0;

        useEffect(Click.IsExecuting) {
            Console.WriteLine($"Command executing with value = {clicked}");
        }

        <Button Click={async () => {
            int res = await Click.Execute(clicked++);
            Console.WriteLine($"Result: {res}");
        }}>
            <TextBlock Text={$"Clicked {clicked} times"} />
        </Button>
        """;

    private const string SharedAkcss =
        """
        @utilities {
            .w-(double value) {
                Width: value;
            }

            .px-(double width) {
                Padding: (horizontal: Amx.DynamicResource<double>("--spacing") * width);
            }

            .hidden {
                IsVisible: false;
            }

            Button.primary {
                Background: "#1976D2";
                Foreground: White;
                FontWeight: Bold;
                Padding: (8, 16);
            }

            .surface {
                Background: "#FFFFFF";
                CornerRadius: 4;
                BoxShadow: "0 2px 4px rgba(0,0,0,0.1)";
            }

            .shadow {
                BoxShadow: "0 4px 8px rgba(0,0,0,0.2)";
            }
        }
        """;

    private const string DashboardPageAkcss =
        """
        @using Demo.Pages;
        @using Demo.Styles.Shared.akcss;

        .card {
            @apply surface shadow;
            Margin: 10;
            Padding: (12, 16);
        }

        DashboardPage {
            Background: "#FAFAFA";
        }

        .tabBtn {
            @apply primary;
            HorizontalAlignment: Stretch;
            Margin: (vertical: 2);
        }

        .sidebar {
            @apply surface;
            Padding: (top: 20, left: 10, right: 10);
        }
        """;
}
