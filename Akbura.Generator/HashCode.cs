using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akbura;
internal static class HashCode
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Combine(int a, int b) => (a + 4) ^ b;

    public static int Combine(int a, int b, int c) => (a + 4) ^ (b + 7) ^ c;

    public static int Combine(int a, int b, int c, int d) => (a + 4) ^ (b + 7) ^ (c + 11) ^ d;

    public static int Combine<T1, T2>(T1 a, T2 b) => Combine(a?.GetHashCode() ?? 0, b?.GetHashCode() ?? 0);

    public static int Combine<T1, T2, T3>(T1 a, T2 b, T3 c) => Combine(a?.GetHashCode() ?? 0, b?.GetHashCode() ?? 0, c?.GetHashCode() ?? 0);
    public static int Combine<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d) => Combine(a?.GetHashCode() ?? 0, b?.GetHashCode() ?? 0, c?.GetHashCode() ?? 0, d?.GetHashCode() ?? 0);
}
