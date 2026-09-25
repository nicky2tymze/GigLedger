using System.Reflection;
using GigLedger.Core;

namespace GigLedger.Tests;

/// <summary>SDD 9.4: rules about the shape of the code, not its answers.</summary>
public class StructuralTests
{
    private static readonly Type[] Floating = [typeof(double), typeof(float), typeof(double?), typeof(float?)];

    [Fact]
    public void NFR5_NoFloatingPointAnywhereInCoresPublicSurface()
    {
        var offenders = new List<string>();
        foreach (var type in typeof(Calculations).Assembly.GetExportedTypes())
        {
            foreach (var p in type.GetProperties())
                if (Floating.Contains(p.PropertyType)) offenders.Add($"{type.Name}.{p.Name}");
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (Floating.Contains(m.ReturnType)) offenders.Add($"{type.Name}.{m.Name} returns");
                foreach (var a in m.GetParameters())
                    if (Floating.Contains(a.ParameterType)) offenders.Add($"{type.Name}.{m.Name}({a.Name})");
            }
        }
        Assert.True(offenders.Count == 0, "Floating point in Core: " + string.Join(", ", offenders));
    }
}
