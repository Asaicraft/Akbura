using System;

namespace Akbura.Language.Binder;

[Flags]
internal enum AkburaBinderFlags
{
    None = 0,
    InComponent = 1 << 0,
    InMarkup = 1 << 1,
    InAkcss = 1 << 2,
    InAkcssStyle = 1 << 3,
    InAkcssUtility = 1 << 4,
    InCSharpProbe = 1 << 5,
    InCSharpBlock = 1 << 6,
}
