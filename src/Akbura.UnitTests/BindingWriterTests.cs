using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using CSharpPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.UnitTests;

public sealed class BindingWriterTests
{

    [Fact]
    public void ElementName_VisibleLocalShadowsClassMember_AndClassMemberCrossesScopes()
    {
        // Avalonia path: #header.Name
        var fixture = CreateFixture();
        var elementType = fixture.GetRequiredType("Demo.Element");
        var extension = CreateElementNameCompiledBinding(
            fixture,
            elementType,
            "#header.Name");
        var elements = new[]
        {
            new BindingElementReference(
                "header",
                "this.header",
                scopeId: 0,
                isClassMember: true),
            new BindingElementReference(
                "header",
                "__localHeader",
                scopeId: 7,
                isClassMember: false),
        };
        var nextCachedPathId = 0;

        var localPlan = CreatePlan(
            fixture,
            extension,
            scopeId: 7,
            nameScopeExpression: null,
            elements,
            ref nextCachedPathId);
        var otherScopePlan = CreatePlan(
            fixture,
            extension,
            scopeId: 8,
            nameScopeExpression: null,
            elements,
            ref nextCachedPathId);

        Assert.True(localPlan.IsValid);
        Assert.Equal("__localHeader", localPlan.SourceExpression);
        Assert.Equal(1, localPlan.PathElementStart);
        Assert.True(localPlan.HasCachedPath);
        Assert.Equal(0, localPlan.CachedPathId);

        Assert.True(otherScopePlan.IsValid);
        Assert.Equal("this.header", otherScopePlan.SourceExpression);
        Assert.Equal(1, otherScopePlan.PathElementStart);
        Assert.True(otherScopePlan.HasCachedPath);
        Assert.Equal(1, otherScopePlan.CachedPathId);
        Assert.Equal(2, nextCachedPathId);

        var cachedPath = WriteCachedPathField(fixture, localPlan);
        var binding = WriteBinding(
            fixture,
            localPlan,
            CreateWriteContext(nameScopeExpression: null, scopeId: 7));

        Assert.DoesNotContain(".ElementName(", cachedPath);
        Assert.Contains("\"Name\"", cachedPath);
        Assert.Equal(
            "new global::Avalonia.Data.CompiledBinding(s_bindingPath0) { Source = __localHeader }",
            binding);
    }

    [Fact]
    public void ReflectionElementName_UsesSourceAndWritesOnlyThePathTail()
    {
        // Avalonia path (whitespace-insensitive): #header.Name
        var fixture = CreateFixture();
        var elementType = fixture.GetRequiredType("Demo.Element");
        const string path = "  #header  .  Name";
        var extension = CreateElementNameBinding(
            fixture,
            elementType,
            path,
            MarkupBindingKind.Reflection);
        var elements = new[]
        {
            new BindingElementReference(
                "header",
                "__header",
                scopeId: 3,
                isClassMember: false),
        };
        var nextCachedPathId = 0;

        var plan = CreatePlan(
            fixture,
            extension,
            scopeId: 3,
            nameScopeExpression: null,
            elements,
            ref nextCachedPathId);

        Assert.True(plan.IsValid);
        Assert.Equal("__header", plan.SourceExpression);
        Assert.Equal(1, plan.PathElementStart);
        Assert.Equal(path.IndexOf("Name", StringComparison.Ordinal), plan.ReflectionPathStart);
        Assert.Same(path, plan.Binding.Path);
        Assert.False(plan.HasCachedPath);
        Assert.Equal(0, nextCachedPathId);
        Assert.Equal(
            "new global::Avalonia.Data.Binding(\"Name\") { Source = __header }",
            WriteBinding(
                fixture,
                plan,
                CreateWriteContext(nameScopeExpression: null, scopeId: 3)));
    }

    [Fact]
    public void ElementName_FromAnotherLocalScope_IsInlineAndRequiresNameScope()
    {
        // Avalonia path: #header.Name
        var fixture = CreateFixture();
        var elementType = fixture.GetRequiredType("Demo.Element");
        var extension = CreateElementNameCompiledBinding(
            fixture,
            elementType,
            "#header.Name");
        var elements = new[]
        {
            new BindingElementReference(
                "header",
                "__templateHeader",
                scopeId: 1,
                isClassMember: false),
        };
        var nextCachedPathId = 0;

        var inlinePlan = CreatePlan(
            fixture,
            extension,
            scopeId: 2,
            nameScopeExpression: "__nameScope",
            elements,
            ref nextCachedPathId);
        var invalidPlan = CreatePlan(
            fixture,
            extension,
            scopeId: 2,
            nameScopeExpression: null,
            elements,
            ref nextCachedPathId);

        Assert.True(inlinePlan.IsValid);
        Assert.Null(inlinePlan.SourceExpression);
        Assert.Equal(0, inlinePlan.PathElementStart);
        Assert.False(inlinePlan.HasCachedPath);
        Assert.Equal(-1, inlinePlan.CachedPathId);
        Assert.Equal(string.Empty, WriteCachedPathField(fixture, inlinePlan));

        var binding = WriteBinding(
            fixture,
            inlinePlan,
            CreateWriteContext("__nameScope", scopeId: 2));

        Assert.Contains(
            "new global::Avalonia.Data.CompiledBinding(new global::Avalonia.Data.CompiledBindingPathBuilder()",
            binding);
        Assert.Contains(".ElementName(__nameScope, \"header\")", binding);
        Assert.Contains("\"Name\"", binding);
        Assert.Contains(".Build())", binding);
        Assert.DoesNotContain("Source =", binding);

        Assert.False(invalidPlan.IsValid);
        Assert.False(invalidPlan.HasCachedPath);
        Assert.Equal(-1, invalidPlan.CachedPathId);
        Assert.Equal(0, nextCachedPathId);
    }

    [Fact]
    public void ElementNameProperty_UsesDirectSourceOrInlineNameScope_AndIsNotAnInitializer()
    {
        // Avalonia long form: {CompiledBinding Path=Name, ElementName=header}
        var fixture = CreateFixture();
        var elementType = fixture.GetRequiredType("Demo.Element");
        var nameProperty = GetProperty(
            elementType,
            "Name");
        var elementNameProperty = new MarkupExtensionPropertyValue(
            name: "ElementName",
            value: "header",
            property: default,
            type: new CSharpSymbolDefinition(
                fixture.Compilation.GetSpecialType(
                    SpecialType.System_String)),
            operation: default,
            conversion: default,
            convertedValue: "header",
            nestedValue: null);
        var extension = CreateBindingExtension(
            fixture,
            MarkupBindingKind.Compiled,
            "Name",
            elementType,
            ImmutableArray.Create(
                CreatePropertyElement(nameProperty)),
            ImmutableArray.Create(elementNameProperty));
        var elements = new[]
        {
            new BindingElementReference(
                "header",
                "__header",
                scopeId: 4,
                isClassMember: false),
        };
        var nextCachedPathId = 0;

        var directPlan = CreatePlan(
            fixture,
            extension,
            scopeId: 4,
            nameScopeExpression: "__nameScope",
            elements,
            ref nextCachedPathId);
        var crossScopePlan = CreatePlan(
            fixture,
            extension,
            scopeId: 5,
            nameScopeExpression: "__nameScope",
            elements,
            ref nextCachedPathId);

        Assert.True(directPlan.IsValid);
        Assert.True(directPlan.HasCachedPath);
        Assert.Equal("__header", directPlan.SourceExpression);
        Assert.Equal(
            0,
            directPlan.ConsumedElementNamePropertyIndex);
        Assert.Equal(
            -1,
            directPlan.ExplicitElementNamePathPropertyIndex);

        var directBinding = WriteBinding(
            fixture,
            directPlan,
            CreateWriteContext(
                "__nameScope",
                scopeId: 4));

        Assert.Equal(
            "new global::Avalonia.Data.CompiledBinding(s_bindingPath0) { Source = __header }",
            directBinding);
        Assert.DoesNotContain(
            "ElementName =",
            directBinding);

        Assert.True(crossScopePlan.IsValid);
        Assert.False(crossScopePlan.HasCachedPath);
        Assert.Null(crossScopePlan.SourceExpression);
        Assert.Equal(
            0,
            crossScopePlan.ConsumedElementNamePropertyIndex);
        Assert.Equal(
            0,
            crossScopePlan.ExplicitElementNamePathPropertyIndex);
        Assert.Equal(1, nextCachedPathId);

        var crossScopeBinding = WriteBinding(
            fixture,
            crossScopePlan,
            CreateWriteContext(
                "__nameScope",
                scopeId: 5));

        Assert.Contains(
            ".ElementName(__nameScope, \"header\")",
            crossScopeBinding);
        Assert.DoesNotContain(
            "ElementName =",
            crossScopeBinding);
        Assert.DoesNotContain(
            "Source =",
            crossScopeBinding);
    }

    [Fact]
    public void Indexer_ConstantArgumentsConsumeCacheIds_DynamicArgumentsStayInline()
    {
        // Avalonia path: Items[0]
        // Akbura expression-index extension: Items[index]
        var fixture = CreateFixture();
        var constantExtension = CreateIndexerCompiledBinding(
            fixture,
            argumentText: "0",
            convertedValue: 0);
        var dynamicExtension = CreateIndexerCompiledBinding(
            fixture,
            argumentText: "index",
            convertedValue: null);
        var nextCachedPathId = 4;

        var firstConstantPlan = CreatePlan(
            fixture,
            constantExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);
        var dynamicPlan = CreatePlan(
            fixture,
            dynamicExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);
        var secondConstantPlan = CreatePlan(
            fixture,
            constantExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        Assert.True(firstConstantPlan.IsValid);
        Assert.True(firstConstantPlan.HasCachedPath);
        Assert.Equal(4, firstConstantPlan.CachedPathId);

        Assert.True(dynamicPlan.IsValid);
        Assert.False(dynamicPlan.HasCachedPath);
        Assert.Equal(-1, dynamicPlan.CachedPathId);
        Assert.Equal(string.Empty, WriteCachedPathField(fixture, dynamicPlan));

        Assert.True(secondConstantPlan.IsValid);
        Assert.True(secondConstantPlan.HasCachedPath);
        Assert.Equal(5, secondConstantPlan.CachedPathId);
        Assert.Equal(6, nextCachedPathId);

        var dynamicBinding = WriteBinding(
            fixture,
            dynamicPlan,
            CreateWriteContext(nameScopeExpression: null, scopeId: 0));

        Assert.Contains("CompiledBindingPathBuilder", dynamicBinding);
        Assert.Contains(
            "__source)[index]",
            dynamicBinding);
        Assert.Contains(".Build())", dynamicBinding);
        Assert.DoesNotContain(
            "static __source => ((global::Demo.ItemCollection)__source)[index]",
            dynamicBinding);
    }

    [Fact]
    public void CachedPathField_UsesNonGenericClrPropertyInfo_AndBindingReusesTheField()
    {
        // Avalonia path: Child.Name
        var fixture = CreateFixture();
        var viewModelType = fixture.GetRequiredType("Demo.ViewModel");
        var childProperty = GetProperty(viewModelType, "Child");
        var nameProperty = GetProperty(
            fixture.GetRequiredType("Demo.Child"),
            "Name");
        var extension = CreateBindingExtension(
            fixture,
            MarkupBindingKind.Compiled,
            "Child.Name",
            viewModelType,
            ImmutableArray.Create(
                CreatePropertyElement(childProperty),
                CreatePropertyElement(nameProperty)));
        var nextCachedPathId = 0;
        var plan = CreatePlan(
            fixture,
            extension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        var field = WriteCachedPathField(fixture, plan);
        var binding = WriteBinding(
            fixture,
            plan,
            CreateWriteContext(nameScopeExpression: null, scopeId: 0));

        Assert.True(plan.IsValid);
        Assert.True(plan.HasCachedPath);
        Assert.Equal(0, plan.CachedPathId);
        Assert.Equal(1, nextCachedPathId);
        Assert.StartsWith(
            "private static readonly global::Avalonia.Data.CompiledBindingPath s_bindingPath0 = " +
            "new global::Avalonia.Data.CompiledBindingPathBuilder()",
            field,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Property(new global::Avalonia.Data.Core.ClrPropertyInfo(",
            field);
        Assert.DoesNotContain("ClrPropertyInfo<", field);
        Assert.EndsWith(".Build();\n", field, StringComparison.Ordinal);
        Assert.Equal(
            "new global::Avalonia.Data.CompiledBinding(s_bindingPath0)",
            binding);

        AssertGeneratedCSharpCompiles(
            fixture,
            field,
            binding);
    }

    [Fact]
    public void ValueTypeMembers_DoNotWriteSetters_AndGeneratedPathsCompile()
    {
        // Avalonia paths: Value and [0]
        // Akbura CLR-field extension: Field
        var fixture = CreateFixture();
        var structType = fixture.GetRequiredType("Demo.StructModel");
        var property = GetProperty(structType, "Value");
        var field = Assert.Single(
            structType.GetMembers("Field")
                .OfType<IFieldSymbol>());
        var indexer = Assert.Single(
            structType.GetMembers()
                .OfType<CSharpPropertySymbol>(),
            static member => member.IsIndexer);
        var indexerParameter = Assert.Single(indexer.Parameters);
        var indexerArgument = new MarkupBindingPathArgument(
            "0",
            new CSharpSymbolDefinition(indexerParameter),
            new CSharpSymbolDefinition(indexerParameter.Type),
            operation: default,
            conversion: default,
            convertedValue: 0);
        var paths = new (string Text, MarkupBindingPathElement Element)[]
        {
            ("Value", CreatePropertyElement(property)),
            (
                "Field",
                new MarkupBindingPathElement(
                    MarkupBindingPathElementKind.Field,
                    "Field",
                    new CSharpSymbolDefinition(field),
                    new CSharpSymbolDefinition(field.Type))),
            (
                "[0]",
                new MarkupBindingPathElement(
                    MarkupBindingPathElementKind.Indexer,
                    "[0]",
                    new CSharpSymbolDefinition(indexer),
                    new CSharpSymbolDefinition(indexer.Type),
                    arguments: default,
                    ImmutableArray.Create(indexerArgument))),
        };
        var nextCachedPathId = 0;

        foreach (var (text, element) in paths)
        {
            var extension = CreateBindingExtension(
                fixture,
                MarkupBindingKind.Compiled,
                text,
                structType,
                ImmutableArray.Create(element));
            var plan = CreatePlan(
                fixture,
                extension,
                scopeId: 0,
                nameScopeExpression: null,
                Array.Empty<BindingElementReference>(),
                ref nextCachedPathId);
            var cachedPath = WriteCachedPathField(fixture, plan);
            var binding = WriteBinding(
                fixture,
                plan,
                CreateWriteContext(
                    nameScopeExpression: null,
                    scopeId: 0));

            Assert.True(plan.IsValid);
            Assert.True(plan.HasCachedPath);
            Assert.DoesNotContain("__value", cachedPath);
            Assert.Contains(", null, typeof(", cachedPath);
            AssertGeneratedCSharpCompiles(
                fixture,
                cachedPath,
                binding);
        }
    }

    [Fact]
    public void UntypedAncestor_WritesNullForgivingLiteral_AndCompiles()
    {
        // Avalonia path: $parent[2]
        var fixture = CreateFixture();
        var sourceType = fixture.GetRequiredType("Demo.ViewModel");
        var extension = CreateBindingExtension(
            fixture,
            MarkupBindingKind.Compiled,
            "$parent[2]",
            sourceType,
            ImmutableArray.Create(
                new MarkupBindingPathElement(
                    MarkupBindingPathElementKind.Ancestor,
                    "$parent[2]",
                    level: 2)));
        var nextCachedPathId = 0;
        var plan = CreatePlan(
            fixture,
            extension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);
        var cachedPath = WriteCachedPathField(fixture, plan);
        var binding = WriteBinding(
            fixture,
            plan,
            CreateWriteContext(
                nameScopeExpression: null,
                scopeId: 0));

        Assert.Contains(".Ancestor(null!, 2)", cachedPath);
        AssertGeneratedCSharpCompiles(
            fixture,
            cachedPath,
            binding);
    }

    [Fact]
    public void UnsignedEnumAboveInt64Max_WritesUncheckedEnumCast_AndCompiles()
    {
        // Avalonia empty path: .
        var fixture = CreateFixture();
        var sourceType = fixture.GetRequiredType("Demo.ViewModel");
        var enumType = fixture.GetRequiredType("Demo.HugeEnum");
        var converterParameter = new MarkupExtensionPropertyValue(
            name: "ConverterParameter",
            value: "18446744073709551615",
            property: default,
            type: new CSharpSymbolDefinition(enumType),
            operation: default,
            conversion: default,
            convertedValue: ulong.MaxValue,
            nestedValue: null);
        var extension = CreateBindingExtension(
            fixture,
            MarkupBindingKind.Compiled,
            string.Empty,
            sourceType,
            ImmutableArray<MarkupBindingPathElement>.Empty,
            ImmutableArray.Create(converterParameter));
        var nextCachedPathId = 0;
        var plan = CreatePlan(
            fixture,
            extension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);
        var cachedPath = WriteCachedPathField(fixture, plan);
        var binding = WriteBinding(
            fixture,
            plan,
            CreateWriteContext(
                nameScopeExpression: null,
                scopeId: 0));

        Assert.Contains(
            "ConverterParameter = unchecked((global::Demo.HugeEnum)18446744073709551615UL)",
            binding);
        AssertGeneratedCSharpCompiles(
            fixture,
            cachedPath,
            binding);
    }

    [Fact]
    public void NestedElementNameBinding_UsesWriterElementReferencesForDirectSource()
    {
        // Outer path: Child.Name
        // Nested Avalonia path: #header.Name
        var fixture = CreateFixture();
        var viewModelType = fixture.GetRequiredType("Demo.ViewModel");
        var childProperty = GetProperty(viewModelType, "Child");
        var nameProperty = GetProperty(
            fixture.GetRequiredType("Demo.Child"),
            "Name");
        var nestedBinding = CreateElementNameCompiledBinding(
            fixture,
            fixture.GetRequiredType("Demo.Element"),
            "#header.Name");
        var converterParameter = new MarkupExtensionPropertyValue(
            name: "ConverterParameter",
            value: nestedBinding.RawText,
            property: default,
            type: new CSharpSymbolDefinition(
                fixture.Compilation.GetSpecialType(
                    SpecialType.System_Object)),
            operation: default,
            conversion: default,
            convertedValue: null,
            nestedValue: nestedBinding);
        var extension = CreateBindingExtension(
            fixture,
            MarkupBindingKind.Compiled,
            "Child.Name",
            viewModelType,
            ImmutableArray.Create(
                CreatePropertyElement(childProperty),
                CreatePropertyElement(nameProperty)),
            ImmutableArray.Create(converterParameter));
        var elements = new[]
        {
            new BindingElementReference(
                "header",
                "__header",
                scopeId: 11,
                isClassMember: false),
        };
        var nextCachedPathId = 0;
        var plan = CreatePlan(
            fixture,
            extension,
            scopeId: 11,
            nameScopeExpression: null,
            elements,
            ref nextCachedPathId);

        var binding = WriteBinding(
            fixture,
            plan,
            CreateWriteContext(
                nameScopeExpression: null,
                scopeId: 11),
            elements);

        Assert.True(plan.IsValid);
        Assert.Contains("Source = __header", binding);
        Assert.DoesNotContain("default!", binding);
        Assert.DoesNotContain(".ElementName(", binding);
    }

    [Fact]
    public void AllModeledPathKinds_WriteExpectedBuilderCalls_AndCompileTogether()
    {
        // Avalonia path:
        // !$parent[Demo.Element;2].((Demo.ViewModel)DataContext).Items[3]?.LoadAsync^.Observable^.IsTrue
        //
        // Alternative roots cannot occur in the same path, so this test also covers:
        // #header.Name
        // $self
        // $templatedParent (Akbura shorthand)
        // $self?.(Grid.Column)
        // Matrix[1, 2]
        var fixture = CreateFixture();
        var elementType =
            fixture.GetRequiredType("Demo.Element");
        var viewModelType =
            fixture.GetRequiredType("Demo.ViewModel");
        var itemCollectionType =
            fixture.GetRequiredType("Demo.ItemCollection");
        var itemType =
            fixture.GetRequiredType("Demo.Item");
        var streamContainerType =
            fixture.GetRequiredType(
                "Demo.StreamContainer");
        var leafType =
            fixture.GetRequiredType("Demo.Leaf");
        var hostType =
            fixture.GetRequiredType(
                "Demo.BindingWriterHost");
        var gridType =
            fixture.GetRequiredType(
                "Avalonia.Controls.Grid");
        var dataContextProperty =
            GetProperty(elementType, "DataContext");
        var itemsProperty =
            GetProperty(viewModelType, "Items");
        var matrixProperty =
            GetProperty(viewModelType, "Matrix");
        var loadAsyncProperty =
            GetProperty(itemType, "LoadAsync");
        var observableProperty =
            GetProperty(
                streamContainerType,
                "Observable");
        var isTrueField = Assert.Single(
            leafType.GetMembers("IsTrue")
                .OfType<IFieldSymbol>());
        var columnPropertyField = Assert.Single(
            gridType.GetMembers("ColumnProperty")
                .OfType<IFieldSymbol>());
        var indexer = Assert.Single(
            itemCollectionType.GetMembers()
                .OfType<CSharpPropertySymbol>(),
            static property => property.IsIndexer);
        var indexerParameter =
            Assert.Single(indexer.Parameters);
        var indexerArgument =
            new MarkupBindingPathArgument(
                "3",
                new CSharpSymbolDefinition(
                    indexerParameter),
                new CSharpSymbolDefinition(
                    indexerParameter.Type),
                operation: default,
                conversion: default,
                convertedValue: 3);
        var indexerElement =
            new MarkupBindingPathElement(
                MarkupBindingPathElementKind.Indexer,
                "[3]",
                new CSharpSymbolDefinition(indexer),
                new CSharpSymbolDefinition(indexer.Type),
                arguments: default,
                ImmutableArray.Create(indexerArgument));
        var isTrueElement =
            new MarkupBindingPathElement(
                MarkupBindingPathElementKind.Field,
                "IsTrue",
                new CSharpSymbolDefinition(isTrueField),
                new CSharpSymbolDefinition(
                    isTrueField.Type));
        var complexPath =
            "!$parent[Demo.Element;2]." +
            "((Demo.ViewModel)DataContext)." +
            "Items[3]?.LoadAsync^." +
            "Observable^.IsTrue";
        var complexExtension =
            CreateBindingExtension(
                fixture,
                MarkupBindingKind.Compiled,
                complexPath,
                viewModelType,
                ImmutableArray.Create(
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind.Not,
                        "!"),
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind.Ancestor,
                        "$parent[Demo.Element;2]",
                        type: new CSharpSymbolDefinition(
                            elementType),
                        arguments: ImmutableArray.Create(
                            "Demo.Element",
                            "2"),
                        level: 2),
                    CreatePropertyElement(
                        dataContextProperty),
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind.TypeCast,
                        "((Demo.ViewModel)DataContext)",
                        type: new CSharpSymbolDefinition(
                            viewModelType)),
                    CreatePropertyElement(itemsProperty),
                    indexerElement,
                    CreatePropertyElement(
                        loadAsyncProperty,
                        acceptsNull: true),
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind.StreamTask,
                        "^",
                        type: new CSharpSymbolDefinition(
                            streamContainerType)),
                    CreatePropertyElement(
                        observableProperty),
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind
                            .StreamObservable,
                        "^",
                        type: new CSharpSymbolDefinition(
                            leafType)),
                    isTrueElement));

        var nextCachedPathId = 0;
        var complexPlan = CreatePlan(
            fixture,
            complexExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        var elementNameExtension =
            CreateElementNameCompiledBinding(
                fixture,
                elementType,
                "#header.Name");
        var elementNamePlan = CreatePlan(
            fixture,
            elementNameExtension,
            scopeId: 0,
            nameScopeExpression: "__nameScope",
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        var selfExtension =
            CreateBindingExtension(
                fixture,
                MarkupBindingKind.Compiled,
                "$self",
                hostType,
                ImmutableArray.Create(
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind.Self,
                        "$self",
                        type: new CSharpSymbolDefinition(
                            hostType))));
        var selfPlan = CreatePlan(
            fixture,
            selfExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        var templatedParentExtension =
            CreateBindingExtension(
                fixture,
                MarkupBindingKind.Compiled,
                "$templatedParent",
                elementType,
                ImmutableArray.Create(
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind
                            .TemplatedParent,
                        "$templatedParent",
                        type: new CSharpSymbolDefinition(
                            elementType))));
        var templatedParentPlan = CreatePlan(
            fixture,
            templatedParentExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        var intType =
            fixture.Compilation.GetSpecialType(
                SpecialType.System_Int32);
        var attachedPropertyExtension =
            CreateBindingExtension(
                fixture,
                MarkupBindingKind.Compiled,
                "$self?.(Grid.Column)",
                gridType,
                ImmutableArray.Create(
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind.Self,
                        "$self",
                        type: new CSharpSymbolDefinition(
                            gridType)),
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind
                            .AttachedProperty,
                        "(Grid.Column)",
                        new CSharpSymbolDefinition(
                            columnPropertyField),
                        new CSharpSymbolDefinition(
                            intType),
                        acceptsNull: true)));
        var attachedPropertyPlan = CreatePlan(
            fixture,
            attachedPropertyExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        var arrayArguments =
            ImmutableArray.Create(
                new MarkupBindingPathArgument(
                    "1",
                    parameter: default,
                    new CSharpSymbolDefinition(intType),
                    operation: default,
                    conversion: default,
                    convertedValue: 1),
                new MarkupBindingPathArgument(
                    "2",
                    parameter: default,
                    new CSharpSymbolDefinition(intType),
                    operation: default,
                    conversion: default,
                    convertedValue: 2));
        var arrayExtension =
            CreateBindingExtension(
                fixture,
                MarkupBindingKind.Compiled,
                "Matrix[1, 2]",
                viewModelType,
                ImmutableArray.Create(
                    CreatePropertyElement(matrixProperty),
                    new MarkupBindingPathElement(
                        MarkupBindingPathElementKind.Indexer,
                        "[1, 2]",
                        type: new CSharpSymbolDefinition(
                            itemType),
                        arguments: default,
                        boundArguments: arrayArguments)));
        var arrayPlan = CreatePlan(
            fixture,
            arrayExtension,
            scopeId: 0,
            nameScopeExpression: null,
            Array.Empty<BindingElementReference>(),
            ref nextCachedPathId);

        Assert.True(complexPlan.IsValid);
        Assert.True(complexPlan.HasCachedPath);
        Assert.True(elementNamePlan.IsValid);
        Assert.False(elementNamePlan.HasCachedPath);
        Assert.True(selfPlan.IsValid);
        Assert.True(templatedParentPlan.IsValid);
        Assert.True(attachedPropertyPlan.IsValid);
        Assert.True(arrayPlan.IsValid);
        Assert.Equal(5, nextCachedPathId);

        var complexField =
            WriteCachedPathField(
                fixture,
                complexPlan);
        var selfField =
            WriteCachedPathField(
                fixture,
                selfPlan);
        var templatedParentField =
            WriteCachedPathField(
                fixture,
                templatedParentPlan);
        var attachedPropertyField =
            WriteCachedPathField(
                fixture,
                attachedPropertyPlan);
        var arrayField =
            WriteCachedPathField(
                fixture,
                arrayPlan);
        var context =
            CreateWriteContext(
                "__nameScope",
                scopeId: 0);
        var complexBinding =
            WriteBinding(
                fixture,
                complexPlan,
                context);
        var elementNameBinding =
            WriteBinding(
                fixture,
                elementNamePlan,
                context);
        var selfBinding =
            WriteBinding(
                fixture,
                selfPlan,
                context);
        var templatedParentBinding =
            WriteBinding(
                fixture,
                templatedParentPlan,
                context);
        var attachedPropertyBinding =
            WriteBinding(
                fixture,
                attachedPropertyPlan,
                context);
        var arrayBinding =
            WriteBinding(
                fixture,
                arrayPlan,
                context);

        Assert.Contains(
            ".Not().Ancestor(" +
            "typeof(global::Demo.Element), 2)",
            complexField);
        Assert.Contains(
            ".TypeCast<global::Demo.ViewModel>()",
            complexField);
        Assert.Contains("\"Item[]\"", complexField);
        Assert.Contains("\"LoadAsync\"", complexField);
        Assert.Contains(
            ".CreateInpcPropertyAccessor, true)",
            complexField);
        Assert.Contains(
            ".StreamTask<global::Demo.StreamContainer>()",
            complexField);
        Assert.Contains("\"Observable\"", complexField);
        Assert.Contains(
            ".StreamObservable<global::Demo.Leaf>()",
            complexField);
        Assert.Contains("\"IsTrue\"", complexField);
        Assert.Contains(
            ".ElementName(__nameScope, \"header\")",
            elementNameBinding);
        Assert.Contains(".Self().Build();", selfField);
        Assert.Contains(
            ".TemplatedParent().Build();",
            templatedParentField);
        Assert.Contains(
            ".Property(" +
            "global::Avalonia.Controls.Grid.ColumnProperty, " +
            "global::Avalonia.Markup.Xaml.MarkupExtensions." +
            "CompiledBindings.PropertyInfoAccessorFactory." +
            "CreateAvaloniaPropertyAccessor, true)",
            attachedPropertyField);
        Assert.Contains(
            ".ArrayElement(new int[] { 1, 2 }, " +
            "typeof(global::Demo.Item))",
            arrayField);

        AssertGeneratedCSharpCompilesTogether(
            fixture,
            [
                complexField,
                selfField,
                templatedParentField,
                attachedPropertyField,
                arrayField,
            ],
            [
                complexBinding,
                elementNameBinding,
                selfBinding,
                templatedParentBinding,
                attachedPropertyBinding,
                arrayBinding,
            ]);
    }

    private static TestFixture CreateFixture()
    {
        const string csharpSource =
            """
            namespace Demo;

            public partial class BindingWriterHost
            {
            }

            public sealed class ViewModel
            {
                public Child Child { get; set; } = new();

                public ItemCollection Items { get; } = new();

                public Item[,] Matrix { get; } =
                    new Item[2, 3];
            }

            public sealed class Child
            {
                public string Name { get; set; } = "";
            }

            public sealed class Element
            {
                public object? DataContext { get; set; }

                public string Name { get; set; } = "";
            }

            public sealed class ItemCollection
            {
                public Item this[int index]
                {
                    get => new();
                    set { }
                }
            }

            public sealed class Item
            {
                public global::System.Threading.Tasks.Task<StreamContainer>
                    LoadAsync { get; } =
                        global::System.Threading.Tasks.Task.FromResult(
                            new StreamContainer());
            }

            public sealed class StreamContainer
            {
                public global::System.IObservable<Leaf> Observable { get; } =
                    null!;
            }

            public sealed class Leaf
            {
                public bool IsTrue;
            }

            public struct StructModel
            {
                public string Value { get; set; }

                public string Field;

                public string this[int index]
                {
                    get => "";
                    set { }
                }
            }

            public enum HugeEnum : ulong
            {
                Max = ulong.MaxValue,
            }
            """;
        var csharpCompilation = CSharpCompilation.Create(
            assemblyName: "BindingWriterTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    csharpSource,
                    CSharpParseOptions.Default.WithLanguageVersion(
                        LanguageVersion.Preview)),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var componentTree = ComponentSyntaxTree.ParseText(
            "using Avalonia.Controls; <Border />",
            "BindingWriterHost.akbura");
        var compilation = new AkburaCompilation(
            csharpCompilation,
            [componentTree],
            rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(componentTree);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(componentTree.GetRoot()).Symbol);
        var environment = BindingWriterEnvironment.Create(
            semanticModel,
            component);

        return new TestFixture(
            csharpCompilation,
            environment);
    }

    private static MarkupExtensionValue CreateElementNameCompiledBinding(
        TestFixture fixture,
        INamedTypeSymbol elementType,
        string path)
    {
        return CreateElementNameBinding(
            fixture,
            elementType,
            path,
            MarkupBindingKind.Compiled);
    }

    private static MarkupExtensionValue CreateElementNameBinding(
        TestFixture fixture,
        INamedTypeSymbol elementType,
        string path,
        MarkupBindingKind kind)
    {
        var nameProperty = GetProperty(
            elementType,
            "Name");

        return CreateBindingExtension(
            fixture,
            kind,
            path,
            elementType,
            ImmutableArray.Create(
                new MarkupBindingPathElement(
                    MarkupBindingPathElementKind.ElementName,
                    "#header",
                    type: new CSharpSymbolDefinition(elementType)),
                CreatePropertyElement(nameProperty)));
    }

    private static MarkupExtensionValue CreateIndexerCompiledBinding(
        TestFixture fixture,
        string argumentText,
        object? convertedValue)
    {
        var viewModelType = fixture.GetRequiredType(
            "Demo.ViewModel");
        var itemsProperty = GetProperty(
            viewModelType,
            "Items");
        var collectionType = fixture.GetRequiredType(
            "Demo.ItemCollection");
        var indexer = Assert.Single(
            collectionType.GetMembers()
                .OfType<CSharpPropertySymbol>(),
            static property => property.IsIndexer);
        var parameter = Assert.Single(
            indexer.Parameters);
        var argument = new MarkupBindingPathArgument(
            argumentText,
            new CSharpSymbolDefinition(parameter),
            new CSharpSymbolDefinition(parameter.Type),
            operation: default,
            conversion: default,
            convertedValue);
        var indexerElement = new MarkupBindingPathElement(
            MarkupBindingPathElementKind.Indexer,
            $"[{argumentText}]",
            new CSharpSymbolDefinition(indexer),
            new CSharpSymbolDefinition(indexer.Type),
            arguments: default,
            ImmutableArray.Create(argument));

        return CreateBindingExtension(
            fixture,
            MarkupBindingKind.Compiled,
            $"Items[{argumentText}]",
            viewModelType,
            ImmutableArray.Create(
                CreatePropertyElement(itemsProperty),
                indexerElement));
    }

    private static MarkupExtensionValue CreateBindingExtension(
        TestFixture fixture,
        MarkupBindingKind kind,
        string path,
        ITypeSymbol sourceType,
        ImmutableArray<MarkupBindingPathElement> pathElements,
        ImmutableArray<MarkupExtensionPropertyValue> properties = default)
    {
        var bindingType = fixture.GetRequiredType(
            kind == MarkupBindingKind.Compiled
                ? "Avalonia.Data.CompiledBinding"
                : "Avalonia.Data.Binding");
        var resultType = pathElements.IsDefaultOrEmpty
            ? sourceType
            : pathElements[^1].Type.Symbol as ITypeSymbol ??
              sourceType;
        var binding = new MarkupBindingValue(
            kind,
            path,
            new CSharpSymbolDefinition(bindingType),
            new CSharpSymbolDefinition(sourceType),
            new CSharpSymbolDefinition(resultType),
            pathElements);

        return new MarkupExtensionValue(
            rawText: path,
            name: kind == MarkupBindingKind.Compiled
                ? "CompiledBinding"
                : "Binding",
            new CSharpSymbolDefinition(bindingType),
            constructor: default,
            provideValueMethod: default,
            new CSharpSymbolDefinition(bindingType),
            arguments: ImmutableArray<MarkupExtensionArgumentValue>.Empty,
            properties: properties.IsDefault
                ? ImmutableArray<MarkupExtensionPropertyValue>.Empty
                : properties,
            binding);
    }

    private static void AssertGeneratedCSharpCompilesTogether(
        TestFixture fixture,
        string[] cachedPathFields,
        string[] bindingExpressions)
    {
        var generatedFields =
            string.Concat(cachedPathFields);
        var generatedBindings =
            string.Join(
                "," + Environment.NewLine,
                bindingExpressions);
        var generatedSource =
            $$"""
            #nullable enable

            namespace Generated;

            internal sealed class BindingWriterOutput
            {
            {{generatedFields}}
                private static global::Avalonia.Data.CompiledBinding[]
                    CreateBindings(
                        global::Avalonia.Controls.INameScope __nameScope)
                {
                    return new global::Avalonia.Data.CompiledBinding[]
                    {
            {{generatedBindings}}
                    };
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview),
            path: "BindingWriterOutput.AllPaths.g.cs");
        var compilation =
            fixture.Compilation.AddSyntaxTrees(
                syntaxTree);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
    }
    private static void AssertGeneratedCSharpCompiles(
        TestFixture fixture,
        string cachedPathField,
        string bindingExpression)
    {
        var generatedSource =
            $$"""
            #nullable enable

            namespace Generated;

            internal sealed class BindingWriterOutput
            {
            {{cachedPathField}}
                private static global::Avalonia.Data.CompiledBinding CreateBinding()
                {
                    return {{bindingExpression}};
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview),
            path: "BindingWriterOutput.g.cs");
        var compilation = fixture.Compilation.AddSyntaxTrees(
            syntaxTree);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
    }
    private static MarkupBindingPathElement CreatePropertyElement(
        CSharpPropertySymbol property,
        bool acceptsNull = false)
    {
        return new MarkupBindingPathElement(
            MarkupBindingPathElementKind.Property,
            property.Name,
            new CSharpSymbolDefinition(property),
            new CSharpSymbolDefinition(property.Type),
            acceptsNull: acceptsNull);
    }

    private static CSharpPropertySymbol GetProperty(
        INamedTypeSymbol type,
        string name)
    {
        return Assert.Single(
            type.GetMembers(name)
                .OfType<CSharpPropertySymbol>());
    }

    private static BindingWritePlan CreatePlan(
        TestFixture fixture,
        MarkupExtensionValue extension,
        int scopeId,
        string? nameScopeExpression,
        BindingElementReference[] elements,
        ref int nextCachedPathId)
    {
        var environment = fixture.Environment;

        return BindingWritePlan.Create(
            in environment,
            extension,
            scopeId,
            nameScopeExpression,
            elements,
            ref nextCachedPathId);
    }

    private static string WriteCachedPathField(
        TestFixture fixture,
        in BindingWritePlan plan)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.Environment;
        var bindingWriter = new BindingWriter(
            codeWriter,
            in environment);

        bindingWriter.WriteCachedPathField(
            in plan);

        return codeWriter.GetText().ToString();
    }

    private static string WriteBinding(
        TestFixture fixture,
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context,
        BindingElementReference[]? elements = null)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.Environment;
        var bindingWriter = new BindingWriter(
            codeWriter,
            in environment,
            elements ?? Array.Empty<BindingElementReference>());

        bindingWriter.WriteBinding(
            in plan,
            in context);

        return codeWriter.GetText().ToString();
    }

    private static MarkupExtensionWriteContext CreateWriteContext(
        string? nameScopeExpression,
        int scopeId)
    {
        return new MarkupExtensionWriteContext(
            targetObjectExpression: "__target",
            targetPropertyExpression: "__property",
            intermediateRootExpression: "__root",
            baseUriExpression: "__baseUri",
            directParentsStackExpression: "__parents",
            fallbackServiceProviderExpression: null,
            nameScopeExpression,
            scopeId);
    }

    private sealed class TestFixture(
        CSharpCompilation compilation,
        BindingWriterEnvironment environment)
    {
        public CSharpCompilation Compilation { get; } =
            compilation;

        public BindingWriterEnvironment Environment { get; } =
            environment;

        public INamedTypeSymbol GetRequiredType(
            string metadataName)
        {
            return Assert.IsAssignableFrom<INamedTypeSymbol>(
                Compilation.GetTypeByMetadataName(metadataName));
        }
    }
}
