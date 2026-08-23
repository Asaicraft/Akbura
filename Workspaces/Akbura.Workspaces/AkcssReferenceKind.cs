namespace Akbura.Workspaces;

internal enum AkcssReferenceKind
{
    None = 0,
    Property,
    PropertyOwnerType,
    StyleDeclaration,
    UtilityDeclaration,
    UtilityParameter,
    ApplyItem,
    ModuleImport,
}
