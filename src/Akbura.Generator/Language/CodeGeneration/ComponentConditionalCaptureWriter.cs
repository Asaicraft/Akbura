using System.Collections.Generic;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentConditionalCaptureWriter
{
    private readonly CodeWriter _writer;

    public ComponentConditionalCaptureWriter(CodeWriter writer)
    {
        _writer = writer;
    }

    public void WritePublishes(in ComponentConditionalBranchPlan branch)
    {
        var values = new CSharpValueWriter(_writer);
        foreach (var capture in branch.Captures)
        {
            _writer.Write("__akburaRenderState.SetRenderCapture<");
            values.WriteTypeNameWithNullableAnnotation(capture.Type);
            _writer.Write(">(").WriteStringLiteral(capture.Key).Write(", ");
            values.WriteIdentifier(capture.Name);
            _writer.WriteLine(");");
        }
    }

    public void WriteClearsInactiveBranches(in ComponentPlan plan, in ComponentConditionalRegionPlan region,
        int selectedBranch)
    {
        var retained = new HashSet<string>();
        var removed = new HashSet<string>();
        for (var branchId = 0; branchId < region.Branches.Length; branchId++)
        {
            CollectCaptureKeys(plan, region, branchId, branchId == selectedBranch ? retained : removed);
        }

        foreach (var key in removed)
        {
            if (!retained.Contains(key))
            {
                _writer.Write("__akburaRenderState.RemoveRenderCapture(").WriteStringLiteral(key).WriteLine(");");
            }
        }
    }

    private static void CollectCaptureKeys(in ComponentPlan plan, in ComponentConditionalRegionPlan region,
        int branchId, HashSet<string> keys)
    {
        foreach (var capture in region.Branches[branchId].Captures)
        {
            keys.Add(capture.Key);
        }

        foreach (var nested in plan.ConditionalRegions)
        {
            if (nested.ParentRegionId == region.Id && nested.ParentBranchId == branchId &&
                nested.RuntimeStorageRootScopeId == region.RuntimeStorageRootScopeId)
            {
                for (var nestedBranch = 0; nestedBranch < nested.Branches.Length; nestedBranch++)
                {
                    CollectCaptureKeys(plan, nested, nestedBranch, keys);
                }
            }
        }
    }

    public void WriteReads(in ComponentPlan plan, int scopeId)
    {
        var values = new CSharpValueWriter(_writer);
        var keys = new HashSet<string>();
        foreach (var region in plan.ConditionalRegions)
        {
            foreach (var branch in region.Branches)
            {
                foreach (var capture in branch.Captures)
                {
                    if (!capture.IsReadByScope(scopeId) || !keys.Add(capture.Key))
                    {
                        continue;
                    }

                    values.WriteTypeNameWithNullableAnnotation(capture.Type);
                    _writer.Write(" ");
                    values.WriteIdentifier(capture.Name);
                    _writer.Write(" = __parentRenderState.GetRenderCapture<");
                    values.WriteTypeNameWithNullableAnnotation(capture.Type);
                    _writer.Write(">(").WriteStringLiteral(capture.Key).Write(")");
                    if (capture.Type.NullableAnnotation == Microsoft.CodeAnalysis.NullableAnnotation.Annotated &&
                        capture.IsKnownNonNullByScope(scopeId))
                    {
                        _writer.Write("!");
                    }

                    _writer.WriteLine(";");
                }
            }
        }
    }
}
