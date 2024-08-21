using System.Numerics;
using Meddle.Utils.Export;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildWater(Material material, string name)
    {
        // TODO: Wavemap stuff maybe? not sure if I want to compute that since its dynamic
        var output = new MaterialBuilder(name)
            .WithDoubleSide(material.RenderBackfaces)
            .WithAlpha(AlphaMode.BLEND, 0.5f)
            .WithBaseColor(new Vector4(1, 1, 1, 0f));;
        
        return output;
    }
}
