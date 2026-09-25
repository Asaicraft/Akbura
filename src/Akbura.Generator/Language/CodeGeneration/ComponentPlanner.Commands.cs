using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private static bool IsDirectCommandReference(IMarkupCommandBindingOperation operation)
        {
            if (operation.TargetKind == MarkupCommandTargetKind.ICommandProperty)
            {
                return false;
            }

            if (operation.HandlerKind != MarkupCommandHandlerKind.DirectReference)
            {
                return false;
            }

            var type = operation.HandlerType.Symbol as INamedTypeSymbol ?? operation.HandlerOperation.Type as INamedTypeSymbol;
            if (type == null)
            {
                return false;
            }

            if (IsRuntimeCommandType(type) || type.Name.StartsWith("__AkburaCommand_", StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var contract in type.AllInterfaces)
            {
                if (IsRuntimeCommandType(contract))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRuntimeCommandType(INamedTypeSymbol type) =>
            type.Name == "IAkburaCommand" && type.ContainingNamespace.ToDisplayString() == "Akbura";

        private IMethodSymbol? GetCommandCallable(IMarkupCommandBindingOperation operation)
        {
            if (operation.HandlerKind != MarkupCommandHandlerKind.DirectReference)
            {
                return null;
            }

            if (operation.HandlerType.Symbol is INamedTypeSymbol { DelegateInvokeMethod: { } invoke })
            {
                return invoke;
            }

            if (FindCommandMethodReference(operation.HandlerOperation.Operation) is { } method)
            {
                return method;
            }

            if (operation.Syntax is MarkupAttributeSyntax attribute &&
                operation.ValueSyntax is MarkupDynamicAttributeValueSyntax value)
            {
                var name = value.Expression.Expression.GetRawCSharpExpression() switch
                {
                    Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    _ => null,
                };
                if (name != null)
                {
                    foreach (var reference in _semanticModel.GetCSharpSymbolReferences(attribute))
                    {
                        if (reference.CSharpDefinition.Symbol is IMethodSymbol candidate && candidate.Name == name)
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }

        private static IMethodSymbol? FindCommandMethodReference(Microsoft.CodeAnalysis.IOperation? operation)
        {
            if (operation is IMethodReferenceOperation method)
            {
                return method.Method;
            }

            if (operation != null)
            {
                foreach (var child in operation.ChildOperations)
                {
                    if (FindCommandMethodReference(child) is { } result)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        private ITypeSymbol? GetCommandHandlerReturnType(IMarkupCommandBindingOperation operation) =>
            GetCommandCallable(operation)?.ReturnType ?? operation.HandlerOperation.Type ??
            operation.ReturnType.Symbol as ITypeSymbol ??
            operation.HandlerResultType.Symbol as ITypeSymbol;

        private ComponentCommandAwaitableKind GetCommandAwaitableKind(IMarkupCommandBindingOperation operation)
        {
            var type = GetCommandHandlerReturnType(operation);
            // An explicitly compatible Task result is a command value, not an implicit async handler.
            if (operation.TargetKind == MarkupCommandTargetKind.DeclaredCommand &&
                type != null && operation.ResultType.Symbol is ITypeSymbol target &&
                _compilation.ClassifyConversion(type, target).IsImplicit)
            {
                return ComponentCommandAwaitableKind.None;
            }

            if (type is not INamedTypeSymbol named ||
                named.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks")
            {
                return ComponentCommandAwaitableKind.None;
            }

            return named.Name switch
            {
                "Task" => ComponentCommandAwaitableKind.Task,
                "ValueTask" => ComponentCommandAwaitableKind.ValueTask,
                _ => ComponentCommandAwaitableKind.None,
            };
        }

        private ITypeSymbol? GetCommandAwaitableResultType(IMarkupCommandBindingOperation operation) =>
            GetCommandAwaitableKind(operation) != ComponentCommandAwaitableKind.None &&
            GetCommandHandlerReturnType(operation) is INamedTypeSymbol { TypeArguments.Length: 1 } named
                ? named.TypeArguments[0] : null;

        private MarkupCommandHandlerKind GetCommandHandlerKind(IMarkupCommandBindingOperation operation)
        {
            return operation.HandlerKind == MarkupCommandHandlerKind.DirectReference &&
                !IsDirectCommandReference(operation) && GetCommandCallable(operation) == null
                ? MarkupCommandHandlerKind.Expression : operation.HandlerKind;
        }

        private MarkupCommandArgumentMode GetCommandArgumentMode(IMarkupCommandBindingOperation operation)
        {
            if (operation.HandlerKind != MarkupCommandHandlerKind.DirectReference || IsDirectCommandReference(operation))
            {
                return operation.ArgumentMode;
            }

            return GetCommandCallable(operation) is { Parameters.Length: > 0 }
                ? MarkupCommandArgumentMode.ReceivesCommandArgument : MarkupCommandArgumentMode.IgnoresCommandArgument;
        }

        private MarkupCommandResultMode GetCommandResultMode(IMarkupCommandBindingOperation operation)
        {
            if (operation.ResultMode != MarkupCommandResultMode.Unknown || IsDirectCommandReference(operation))
            {
                return operation.ResultMode;
            }

            var type = GetCommandHandlerReturnType(operation);
            if (type?.SpecialType == SpecialType.System_Void ||
                GetCommandAwaitableKind(operation) != ComponentCommandAwaitableKind.None &&
                GetCommandAwaitableResultType(operation) == null)
            {
                return MarkupCommandResultMode.NoResult;
            }

            return MarkupCommandResultMode.ReturnsResult;
        }
    }
}
