using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed class MarkupForeachBindingTests
{
    [Fact]
    public void Foreach_BindsTypedItemIndexAndMixedLocalsWithoutFlatteningControlFlow()
    {
        var fixture = CreateFixture(
            """
            using System.Collections.Immutable;
            state ImmutableArray<int> array = [1, 2, 3, 4, 5];
            <StackPanel>
                <TextBlock Text="Starting loop" />
                $foreach (var item in array)
                {
                    var doubled = item * 2;
                    if (doubled % 4 == 0) { continue; }
                    if (item % 3 == 0) { break; }
                    <TextBlock Text={$"Current item is {doubled} and index is {@index}"} />
                }
                <TextBlock Text="Ending loop" />
            </StackPanel>
            """);
        var loop = GetLoop(fixture);
        var operation = Assert.IsAssignableFrom<IMarkupForeachOperation>(fixture.SemanticModel.GetOperation(loop));

        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<ITypeSymbol>(operation.IterationType.Symbol).SpecialType);
        Assert.Equal("item", operation.IterationVariableName);
        Assert.Equal(4, operation.Body.Length);
        Assert.IsType<MarkupCodeStatementSyntax>(operation.Body[0].Syntax);
        Assert.IsType<MarkupCodeIfStatementSyntax>(operation.Body[1].Syntax);
        Assert.Single(operation.Body[1].Body);
        Assert.IsType<MarkupCodeStatementSyntax>(operation.Body[1].Body[0].Syntax);
        var content = Assert.IsAssignableFrom<IMarkupContentOperation>(fixture.SemanticModel.GetOperation(fixture.GetRootElement()));
        Assert.Equal(3, content.Content.Length);
        Assert.Same(operation, content.Content[1].ForeachOperation);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("Border")]
    [InlineData("ContentControl")]
    public void Foreach_RejectsScalarDestinationsEvenForASingleton(string owner)
    {
        var fixture = CreateFixture("<" + owner + ">$foreach (var item in new[] { 1 }) { <TextBlock /> }</" + owner + ">");
        var loop = GetLoop(fixture);

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()), diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedForeachContentDestination &&
            ReferenceEquals(diagnostic.Syntax, loop));
    }

    [Fact]
    public void Foreach_AcceptsAGetterOnlyMutableGenericList()
    {
        var fixture = CreateFixture("<ListOwner>$foreach (var item in new[] { 1 }) { <TextBlock Text={item.ToString()} /> }</ListOwner>",
            """
            namespace Demo;
            public sealed class ListOwner : Avalonia.Controls.Control
            {
                [Avalonia.Metadata.Content]
                public System.Collections.Generic.IList<Avalonia.Controls.Control> Children { get; } =
                    new System.Collections.Generic.List<Avalonia.Controls.Control>();
            }
            """);

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("System.Collections.Generic.IReadOnlyList<Avalonia.Controls.Control>")]
    [InlineData("System.Collections.Generic.ICollection<Avalonia.Controls.Control>")]
    [InlineData("Avalonia.Controls.Control[]")]
    public void Foreach_RejectsDestinationsWithoutAnIndexedMutationContract(string type)
    {
        var fixture = CreateFixture("<ListOwner>$foreach (var item in new[] { 1 }) { <TextBlock /> }</ListOwner>",
            "namespace Demo; public sealed class ListOwner : Avalonia.Controls.Control { " +
            "[Avalonia.Metadata.Content] public " + type + " Children => null!; }");

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()), diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedForeachContentDestination);
    }

    [Fact]
    public void Foreach_SourceCannotReadItsOwnIterationVariable()
    {
        var fixture = CreateFixture("<StackPanel>$foreach (var item in item) { <TextBlock /> }</StackPanel>");

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()), diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError &&
            diagnostic.Message.Contains("item", StringComparison.Ordinal));
    }

    [Fact]
    public void Foreach_KeyAndPatternGuardsSeeTheTypedIterationVariable()
    {
        var fixture = CreateFixture(
            """
            param System.Collections.Generic.IEnumerable<Person> people = default!;
            <StackPanel>
                $foreach (Person person in people; key: person.Id)
                {
                    if (person.Name is { } name)
                    {
                        <TextBlock Text={name} />
                    }
                }
            </StackPanel>
            """, "namespace Demo; public sealed class Person { public int Id => 1; public string? Name => null; }");
        var loop = GetLoop(fixture);
        var operation = Assert.IsAssignableFrom<IMarkupForeachOperation>(fixture.SemanticModel.GetOperation(loop));

        Assert.Equal(SpecialType.System_Int32, operation.Key.Type!.SpecialType);
        Assert.Equal("Person", operation.IterationType.Name);
        Assert.Single(operation.Body);
        Assert.Single(operation.Body[0].Body);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void NestedForeach_KeepsOuterItemAndAssignsDistinctSourceIndexNames()
    {
        var fixture = CreateFixture(
            """
            <StackPanel>
                $foreach (var outer in new[] { 1 })
                {
                    $foreach (var inner in new[] { outer })
                    {
                        <TextBlock Text={$"{outer}:{inner}:{@index}"} />
                    }
                }
            </StackPanel>
            """);
        var operation = Assert.IsAssignableFrom<IMarkupForeachOperation>(fixture.SemanticModel.GetOperation(GetLoop(fixture)));
        var nested = Assert.Single(operation.Body).ForeachOperation!;

        Assert.NotEqual(operation.IndexVariableName, nested.IndexVariableName);
        Assert.Equal("inner", nested.IterationVariableName);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<ITypeSymbol>(nested.IterationType.Symbol).SpecialType);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void Foreach_IndexIsReadOnlyAndCannotBeRedeclared()
    {
        var fixture = CreateFixture("<StackPanel>$foreach (var item in new[] { 1 }) { var index = 3; <TextBlock /> }</StackPanel>");

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()), diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ForeachIndexRedeclaration);
        var assigned = CreateFixture("<StackPanel>$foreach (var item in new[] { 1 }) { <TextBlock Text={(index = 1).ToString()} /> }</StackPanel>");
        Assert.Contains(assigned.SemanticModel.GetSemanticDiagnostics(assigned.ComponentTree.GetRoot()), diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError &&
            diagnostic.Message.Contains("read only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Foreach_IndexGenerationRewritesBoundIdentifiersButNotMembersStringsOrNameof()
    {
        var fixture = CreateFixture(
            """
            param System.Collections.Generic.IEnumerable<Person> people = default!;
            <StackPanel>$foreach (var person in people)
            {
                <TextBlock Text={$"{person.@index} {@index} {nameof(index)} @index"} />
            }</StackPanel>
            """, "namespace Demo; public sealed class Person { public int @index => 42; }");
        var loop = GetLoop(fixture);
        var element = fixture.GetRootElement().DescendantNodes().OfType<MarkupElementSyntax>().Single();
        var property = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(fixture.SemanticModel.GetOperation(
            Assert.Single(element.StartTag!.Attributes)));
        var source = CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(property.ValueOperation)!.ToString();

        Assert.Contains("person.@index", source);
        Assert.Contains("{" + CSharpProbeBuilder.GetMarkupLoopIndexName(loop) + "}", source);
        Assert.Contains("{\"index\"}", source);
        Assert.EndsWith(" @index\"", source);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void Foreach_CompletionProbePreservesActiveFragmentAndTypedLoopScope()
    {
        var fixture = CreateFixture("<StackPanel>$foreach (var item in new[] { 1 }) { <TextBlock Text={item.ToString() + index.ToString()} /> }</StackPanel>");
        var expression = fixture.GetRootElement().DescendantNodes().OfType<CSharpExpressionSyntax>().Single();
        var projection = fixture.SemanticModel.CreateCSharpCompletionProjection(expression, 4);

        Assert.Equal(expression.Tokens.ToFullString(), projection.Root.ToFullString().Substring(
            projection.ProjectedSpan.Start, projection.ProjectedSpan.Length));
        Assert.Contains(projection.SymbolOrigins, origin => origin.Name == "item");
        Assert.Contains(projection.SymbolOrigins, origin => origin.Kind == Akbura.Language.Symbols.SymbolKind.MarkupLoopIndex);
    }

    private static MarkupForeachStatementSyntax GetLoop(AkcssActivatorPlannerTests.PlannerFixture fixture) =>
        fixture.GetRootElement().DescendantNodes().OfType<MarkupForeachStatementSyntax>().First();

    [Fact]
    public void Foreach_PatternGuardContinueMakesTheDeclaredLocalAvailableAfterTheGuard()
    {
        var fixture = CreateFixture(
            """
            param System.Collections.Generic.IEnumerable<object> people = default!;
            <StackPanel>$foreach (var item in people)
            {
                if (item is not Person person) { continue; }
                <TextBlock Text={person.Name} />
            }</StackPanel>
            """, "namespace Demo; public sealed class Person { public string Name => string.Empty; }");
        var expression = fixture.GetRootElement().DescendantNodes().OfType<CSharpExpressionSyntax>()
            .Single(syntax => syntax.ToFullString().Contains("person.Name", StringComparison.Ordinal));
        var projection = fixture.SemanticModel.CreateCSharpCompletionProjection(expression, 7);

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
        Assert.Contains(projection.SymbolOrigins, origin => origin.Name == "person");
    }

    [Fact]
    public void Foreach_SimpleRootIdBecomesTypedIterationIdentityWithoutAClrSetter()
    {
        var fixture = CreateFixture("<StackPanel>$foreach (var item in new[] { 1, 2 }) { <TextBlock x.id={item} /> }</StackPanel>");
        var operation = Assert.IsAssignableFrom<IMarkupForeachOperation>(fixture.SemanticModel.GetOperation(GetLoop(fixture)));
        var attribute = fixture.GetRootElement().DescendantNodes().OfType<MarkupAttachedPropertyAttributeSyntax>().Single();
        var identity = Assert.IsAssignableFrom<IMarkupForeachKeyOperation>(fixture.SemanticModel.GetOperation(attribute));

        Assert.True(identity.IsIterationKey);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<ITypeSymbol>(identity.KeyType.Symbol).SpecialType);
        Assert.Equal(identity.ValueOperation.ToDisplayString(), operation.Key.ToDisplayString());
        Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(
            identity.ValueOperation.Type, operation.Key.Type));
        Assert.Same(attribute, operation.KeySyntax);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void Foreach_IdInAMultiRootBodyStaysLocalAndDoesNotChooseTheFirstIdentity()
    {
        var fixture = CreateFixture("<StackPanel>$foreach (var item in new[] { 1 }) { <TextBlock x.id={item} /> <Button /> }</StackPanel>");
        var operation = Assert.IsAssignableFrom<IMarkupForeachOperation>(fixture.SemanticModel.GetOperation(GetLoop(fixture)));
        var attribute = fixture.GetRootElement().DescendantNodes().OfType<MarkupAttachedPropertyAttributeSyntax>().Single();
        var identity = Assert.IsAssignableFrom<IMarkupForeachKeyOperation>(fixture.SemanticModel.GetOperation(attribute));

        Assert.False(identity.IsIterationKey);
        Assert.True(operation.Key.IsDefault);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("index")]
    [InlineData("ambient")]
    [InlineData("item++")]
    [InlineData("null")]
    public void Foreach_RejectsUnstableOrNullIterationKeys(string key)
    {
        var fixture = CreateFixture("state int ambient = 1; <StackPanel>$foreach (var item in new[] { 1 }; key: " +
            key + ") { <TextBlock /> }</StackPanel>");

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()), diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ForeachKeyInvalid);
    }

    [Fact]
    public void Foreach_HeaderAndRawStatementProjectionsPreserveTheirActualSourceSpans()
    {
        var fixture = CreateFixture("<StackPanel>$foreach (var item in new[] { 1 }) { var doubled = item * 2; <TextBlock /> }</StackPanel>");
        var loop = GetLoop(fixture);
        var header = fixture.SemanticModel.CreateCSharpCompletionProjection(loop.Header, 3);
        var code = Assert.IsType<MarkupCodeStatementSyntax>(loop.Body.Content[0]);
        var statement = fixture.SemanticModel.CreateCSharpCompletionProjection(code, 3);

        Assert.Equal(loop.Header.Token.ToFullString(), header.Root.ToFullString().Substring(
            header.ProjectedSpan.Start, header.ProjectedSpan.Length));
        Assert.Equal(code.Token.ToFullString(), statement.Root.ToFullString().Substring(
            statement.ProjectedSpan.Start, statement.ProjectedSpan.Length));
        Assert.Contains(statement.SymbolOrigins, origin => origin.Name == "item");
    }

    [Fact]
    public void Foreach_EachPropertyElementKeepsItsOwnOutputType()
    {
        var fixture = CreateFixture(
            """
            <PairOwner>
                <PairOwner.Numbers>$foreach (var item in new[] { 1 }) { {item} }</PairOwner.Numbers>
                <PairOwner.Controls>$foreach (var item in new[] { 1 }) { <TextBlock /> }</PairOwner.Controls>
            </PairOwner>
            """,
            """
            namespace Demo;
            public sealed class PairOwner : Avalonia.Controls.Control
            {
                public System.Collections.Generic.IList<int> Numbers { get; } = new System.Collections.Generic.List<int>();
                public System.Collections.Generic.IList<Avalonia.Controls.Control> Controls { get; } =
                    new System.Collections.Generic.List<Avalonia.Controls.Control>();
            }
            """);
        var loops = fixture.GetRootElement().DescendantNodes().OfType<MarkupForeachStatementSyntax>()
            .Select(syntax => Assert.IsAssignableFrom<IMarkupForeachOperation>(fixture.SemanticModel.GetOperation(syntax)))
            .ToArray();

        Assert.Equal(2, loops.Length);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<ITypeSymbol>(loops[0].OutputType.Symbol).SpecialType);
        Assert.Equal("Avalonia.Controls.Control", loops[1].OutputType.Symbol!.ToDisplayString());
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreateFixture(string content, string? csharp = null) =>
        AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; using Demo; " + content, csharp);
}
