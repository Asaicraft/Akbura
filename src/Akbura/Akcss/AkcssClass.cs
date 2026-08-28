using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Akbura.Akcss;
public abstract class AkcssClass: AkcssStyle
{
    public abstract void Update(object control);
}
