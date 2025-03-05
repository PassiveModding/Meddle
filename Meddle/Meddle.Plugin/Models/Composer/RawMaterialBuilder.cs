using Meddle.Plugin.Models.Composer.Textures;
using Meddle.Utils.Helpers;
using Meddle.Utils.Materials;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public class RawMaterialBuilder : MaterialBuilder, IVertexPaintMaterialBuilder
{
    public bool VertexPaint { get; }
    
    public RawMaterialBuilder(string name) : base(name)
    {
        IndexOfRefraction = 1.0f;
        VertexPaint = true;
    }
}
