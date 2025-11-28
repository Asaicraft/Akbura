using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Akcss;

public abstract class AkcssUtility : AkcssStyle
{
    public bool IsConditional
    {
        get; set;
    }

    public abstract ImmutableArray<Type> Parameters
    {
        get;
    }

    public abstract void Update(AkburaControlWrapper wrapper, params object[] parameters);
}

public abstract class ZeroAkcssUtility: AkcssUtility
{
    public override ImmutableArray<Type> Parameters => [];

    public abstract void Update(AkburaControlWrapper wrapper);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper);
    }
}

public abstract class AkcssUtility<T1> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters => [typeof(T1)];
    
    public abstract void Update(AkburaControlWrapper wrapper, T1 param1);
    
    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper, (T1)parameters[0]);
    }
}

public abstract class AkcssUtility<T1, T2> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters => [typeof(T1), typeof(T2)];

    public abstract void Update(AkburaControlWrapper wrapper, T1 p1, T2 p2);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1]);
    }
}

public abstract class AkcssUtility<T1, T2, T3> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters => [typeof(T1), typeof(T2), typeof(T3)];

    public abstract void Update(AkburaControlWrapper wrapper, T1 p1, T2 p2, T3 p3);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters => [typeof(T1), typeof(T2), typeof(T3), typeof(T4)];

    public abstract void Update(AkburaControlWrapper wrapper, T1 p1, T2 p2, T3 p3, T4 p4);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7),
         typeof(T8), typeof(T9), typeof(T10)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8],
            (T10)parameters[9]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7),
         typeof(T8), typeof(T9), typeof(T10), typeof(T11)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8],
            (T10)parameters[9],
            (T11)parameters[10]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7),
         typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8],
            (T10)parameters[9],
            (T11)parameters[10],
            (T12)parameters[11]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7),
         typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12, T13 p13);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8],
            (T10)parameters[9],
            (T11)parameters[10],
            (T12)parameters[11],
            (T13)parameters[12]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7),
         typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12, T13 p13, T14 p14);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8],
            (T10)parameters[9],
            (T11)parameters[10],
            (T12)parameters[11],
            (T13)parameters[12],
            (T14)parameters[13]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7),
         typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14), typeof(T15)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12, T13 p13, T14 p14, T15 p15);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8],
            (T10)parameters[9],
            (T11)parameters[10],
            (T12)parameters[11],
            (T13)parameters[12],
            (T14)parameters[13],
            (T15)parameters[14]);
    }
}

public abstract class AkcssUtility<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : AkcssUtility
{
    public override ImmutableArray<Type> Parameters =>
        [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7),
         typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14), typeof(T15), typeof(T16)];

    public abstract void Update(AkburaControlWrapper wrapper,
        T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11,
        T12 p12, T13 p13, T14 p14, T15 p15, T16 p16);

    public override void Update(AkburaControlWrapper wrapper, params object[] parameters)
    {
        Update(wrapper,
            (T1)parameters[0],
            (T2)parameters[1],
            (T3)parameters[2],
            (T4)parameters[3],
            (T5)parameters[4],
            (T6)parameters[5],
            (T7)parameters[6],
            (T8)parameters[7],
            (T9)parameters[8],
            (T10)parameters[9],
            (T11)parameters[10],
            (T12)parameters[11],
            (T13)parameters[12],
            (T14)parameters[13],
            (T15)parameters[14],
            (T16)parameters[15]);
    }
}

