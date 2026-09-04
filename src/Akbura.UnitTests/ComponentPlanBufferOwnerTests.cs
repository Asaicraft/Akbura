using Akbura.Language.CodeGeneration;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.UnitTests;

public sealed class ComponentPlanBufferOwnerTests
{
    [Fact]
    public void MoveToPlan_TransfersBufferOwnership()
    {
        var owner = new ComponentPlanBufferOwner(default);
        var plan = default(ComponentPlan);

        try
        {
            owner.ElementReferences = PooledImmutableList<BindingElementReference>.Create(
            [
                new BindingElementReference(
                    "header",
                    "header",
                    scopeId: 0,
                    isClassMember: true),
            ]);

            plan = owner.MoveToPlan(default);

            owner.Dispose();

            Assert.Single(plan.ElementReferences);
            Assert.Equal("header", plan.ElementReferences[0].Name);
            Assert.Equal("header", plan.ElementReferences[0].Expression);
        }
        finally
        {
            owner.Dispose();
            plan.ReturnToPool();
        }
    }

    [Fact]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        var owner = new ComponentPlanBufferOwner(default)
        {
            ElementReferences = PooledImmutableList<BindingElementReference>.Create(
            [
                new BindingElementReference(
                    "header",
                    "header",
                    scopeId: 0,
                    isClassMember: true),
            ])
        };

        owner.Dispose();
        owner.Dispose();
    }
}
