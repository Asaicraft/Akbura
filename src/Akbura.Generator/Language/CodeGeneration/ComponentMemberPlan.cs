using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using Akbura.Pools;

namespace Akbura.Language.CodeGeneration;

internal enum ComponentParameterKind : byte
{
    Value,
    Collection,
}

[Flags]
internal enum ComponentParameterFlags : byte
{
    None = 0,
    HasDefaultValue = 1 << 0,
    IsContent = 1 << 1,
    ReceivesValue = 1 << 2,
    SendsValue = 1 << 3,
}

internal readonly struct ComponentParameterCollectionPlan
{
    public ComponentParameterCollectionPlan(
        ITypeSymbol propertyType,
        ITypeSymbol elementType,
        INamedTypeSymbol backingType,
        bool observesChanges)
    {
        PropertyType = propertyType;
        ElementType = elementType;
        BackingType = backingType;
        ObservesChanges = observesChanges;
    }

    public ITypeSymbol PropertyType { get; }

    public ITypeSymbol ElementType { get; }

    public INamedTypeSymbol BackingType { get; }

    public bool ObservesChanges { get; }
}

internal readonly struct ComponentParameterPlan
{
    public ComponentParameterPlan(
        int id,
        string name,
        ITypeSymbol type,
        ParamBindingKind bindingKind,
        ComponentParameterKind kind,
        ComponentParameterFlags flags,
        ExpressionSyntax? defaultValue,
        ComponentParameterCollectionPlan collection,
        ParamDeclarationSyntax syntax)
    {
        Id = id;
        Name = name;
        Type = type;
        BindingKind = bindingKind;
        Kind = kind;
        Flags = flags;
        DefaultValue = defaultValue;
        Collection = collection;
        Syntax = syntax;
    }

    public int Id { get; }

    public string Name { get; }

    public ITypeSymbol Type { get; }

    public ParamBindingKind BindingKind { get; }

    public ComponentParameterKind Kind { get; }

    public ComponentParameterFlags Flags { get; }

    public ExpressionSyntax? DefaultValue { get; }

    public ComponentParameterCollectionPlan Collection { get; }

    public ParamDeclarationSyntax Syntax { get; }

    public bool HasDefaultValue =>
        (Flags & ComponentParameterFlags.HasDefaultValue) != 0;

    public bool IsContent =>
        (Flags & ComponentParameterFlags.IsContent) != 0;

    public bool ReceivesValue =>
        (Flags & ComponentParameterFlags.ReceivesValue) != 0;

    public bool SendsValue =>
        (Flags & ComponentParameterFlags.SendsValue) != 0;
}

internal readonly struct ComponentInjectServicePlan
{
    public ComponentInjectServicePlan(
        int id,
        string name,
        ITypeSymbol serviceType,
        bool isOptional,
        InjectDeclarationSyntax syntax)
    {
        Id = id;
        Name = name;
        ServiceType = serviceType;
        IsOptional = isOptional;
        Syntax = syntax;
    }

    public int Id { get; }

    public string Name { get; }

    /// <summary>
    /// Non-nullable generic service type.
    /// </summary>
    public ITypeSymbol ServiceType { get; }

    public bool IsOptional { get; }

    public InjectDeclarationSyntax Syntax { get; }
}

internal enum ComponentStateFactoryKind : byte
{
    Value,
    State,
}

[Flags]
internal enum ComponentStateFlags : byte
{
    None = 0,
    IsReadOnly = 1 << 0,
    UsesHook = 1 << 1,
}

internal readonly struct ComponentStatePlan
{
    public ComponentStatePlan(
        int id,
        string name,
        ITypeSymbol valueType,
        StateBindingKind bindingKind,
        ComponentStateFactoryKind factoryKind,
        ComponentStateFlags flags,
        ExpressionSyntax initializer,
        StateDeclarationSyntax syntax)
    {
        Id = id;
        Name = name;
        ValueType = valueType;
        BindingKind = bindingKind;
        FactoryKind = factoryKind;
        Flags = flags;
        Initializer = initializer;
        Syntax = syntax;
    }

    public int Id { get; }

    public string Name { get; }

    public ITypeSymbol ValueType { get; }

    public StateBindingKind BindingKind { get; }

    public ComponentStateFactoryKind FactoryKind { get; }

    public ComponentStateFlags Flags { get; }

    public ExpressionSyntax Initializer { get; }

    public StateDeclarationSyntax Syntax { get; }

    public bool IsReadOnly => (Flags & ComponentStateFlags.IsReadOnly) != 0;

    public bool UsesHook => (Flags & ComponentStateFlags.UsesHook) != 0;
}

internal readonly struct ComponentCommandParameterPlan
{
    public ComponentCommandParameterPlan(
        int ordinal,
        string name,
        ITypeSymbol type)
    {
        Ordinal = ordinal;
        Name = name;
        Type = type;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public ITypeSymbol Type { get; }
}

internal readonly struct ComponentCommandPlan
{
    public ComponentCommandPlan(
        int id,
        string name,
        ITypeSymbol resultType,
        ComponentPlanRange parameters,
        CommandDeclarationSyntax syntax)
    {
        Id = id;
        Name = name;
        ResultType = resultType;
        Parameters = parameters;
        Syntax = syntax;
    }

    public int Id { get; }

    public string Name { get; }

    public ITypeSymbol ResultType { get; }

    /// <summary>
    /// Range inside <see cref="ComponentMemberPlan.CommandParameters"/>.
    /// </summary>
    public ComponentPlanRange Parameters { get; }

    public CommandDeclarationSyntax Syntax { get; }
}

internal readonly struct ComponentUserMemberPlan
{
    public ComponentUserMemberPlan(
        LocalFunctionStatementSyntax member,
        CSharpStatementSyntax syntax)
    {
        Member = member;
        Syntax = syntax;
    }

    public LocalFunctionStatementSyntax Member { get; }

    public CSharpStatementSyntax Syntax { get; }
}

internal readonly struct ComponentMemberPlan
{
    public ComponentMemberPlan(
        PooledImmutableList<ComponentParameterPlan> parameters,
        PooledImmutableList<ComponentStatePlan> states,
        PooledImmutableList<ComponentInjectServicePlan> services,
        PooledImmutableList<ComponentCommandPlan> commands,
        PooledImmutableList<ComponentCommandParameterPlan> commandParameters,
        PooledImmutableList<ComponentUserMemberPlan> userMembers)
    {
        Parameters = parameters;
        States = states;
        Services = services;
        Commands = commands;
        CommandParameters = commandParameters;
        UserMembers = userMembers;
    }

    public PooledImmutableList<ComponentParameterPlan> Parameters { get; }

    public PooledImmutableList<ComponentStatePlan> States { get; }

    public PooledImmutableList<ComponentInjectServicePlan> Services { get; }

    public PooledImmutableList<ComponentCommandPlan> Commands { get; }

    public PooledImmutableList<ComponentCommandParameterPlan> CommandParameters { get; }

    public PooledImmutableList<ComponentUserMemberPlan> UserMembers { get; }

    public bool IsEmpty =>
        Parameters.IsDefaultOrEmpty &&
        States.IsDefaultOrEmpty &&
        Services.IsDefaultOrEmpty &&
        Commands.IsDefaultOrEmpty &&
        CommandParameters.IsDefaultOrEmpty &&
        UserMembers.IsDefaultOrEmpty;

    public void ReturnToPool()
    {
        Parameters.ReturnToPool();
        States.ReturnToPool();
        Services.ReturnToPool();
        Commands.ReturnToPool();
        CommandParameters.ReturnToPool();
        UserMembers.ReturnToPool();
    }
}
