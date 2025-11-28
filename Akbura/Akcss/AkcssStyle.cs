using Akbura.CompilerAnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Akcss;
public abstract class AkcssStyle
{

    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NameCore))
            {
                return NameCore;
            }

            var type = this.GetType();
            var attributes = type.GetCustomAttributes(typeof(StyleNameAttribute), true);
            if (attributes.Length > 0)
            {
                var styleNameAttribute = (StyleNameAttribute)attributes[0];
                return (NameCore = styleNameAttribute.Name)!;
            }
            else
            {
                return (NameCore = type.Name)!;
            }
        }
    }

    protected string? NameCore
    {
        get; set;
    }

    public bool IsInlined
    {
        get
        {
            if(IsInlinedCore.HasValue)
            {
                return IsInlinedCore.Value;
            }

            var type = this.GetType();
            var attributes = type.GetCustomAttributes(typeof(InlinedStyleAttribute), true);


            return (IsInlinedCore = attributes.Length > 0).Value;
        }
    }

    protected bool? IsInlinedCore
    {
        get; set;
    }

    public abstract void Watch(AkburaControlWrapper wrapper);
}
