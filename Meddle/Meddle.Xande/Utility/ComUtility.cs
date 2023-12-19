using SharpGen.Runtime;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Meddle.Xande.Utility;

internal static class ComUtility
{
    private static ReadOnlyDictionary<Guid, string> GuidTypes { get; }

    static ComUtility()
    {
        var asms = new Assembly[] { typeof(DXGI).Assembly, typeof(D3D11).Assembly };
        GuidTypes = new(
            asms.SelectMany(a => a.ExportedTypes)
            .Select(t => (t, t.GetCustomAttribute<GuidAttribute>()))
            .Where(t => t.Item2 != null)
            .ToDictionary(k => Guid.Parse(k.Item2!.Value), v => v.t.FullName ?? v.t.Name)
        );
    }

    public static unsafe List<(Guid Guid, string TypeName)> GetInterfaces(ComObject unk)
    {
        var ret = new List<(Guid, string)>();
        foreach (var (k, v) in GuidTypes)
        {
            var hr = unk.QueryInterface(k, out _);
            if (hr == Result.NoInterface)
                continue;
            ret.Add((k, v));
        }
        return ret;
    }
}
