using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public abstract class MeddleMaterialBuilder : MaterialBuilder
{
    public MeddleMaterialBuilder(string name) : base(name)
    {
    }
    
    public abstract MeddleMaterialBuilder Apply();
}
