using Avalonia.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.ComponentTree;

public static class ParameterBindingExtension
{
    public static BindingMode ToBindingMode(this ParameterBinding parameterBinding)
    {
        return parameterBinding switch
        {
            
            ParameterBinding.In => BindingMode.OneWay,
            ParameterBinding.Bind => BindingMode.TwoWay,
            ParameterBinding.Out => BindingMode.OneWayToSource,
            _ => BindingMode.OneWay,
        };
    }
}
