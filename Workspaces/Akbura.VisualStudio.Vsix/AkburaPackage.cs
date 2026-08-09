using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.VisualStudio.Vsix;


[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideBindingPath]
[Guid(PackageGuidString)]
public sealed class AkburaPackage :
    AsyncPackage
{
    public const string PackageGuidString = "A018AD96-EA47-4508-B674-9CB2FB9A5175";
}