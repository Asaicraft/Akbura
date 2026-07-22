using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Furioso;

public static class EnvironmentProcessorCount
{
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
    public static int Count => Environment.ProcessorCount;
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
}
