using Meddle.Utils.Materials;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public class RawMaterialBuilder : MaterialBuilder, IVertexPaintMaterialBuilder
{
    public bool VertexPaint { get; }
    
    public RawMaterialBuilder(string name) : base(name)
    {
        IndexOfRefraction = 1.0f;
        VertexPaint = true;
    }
}
