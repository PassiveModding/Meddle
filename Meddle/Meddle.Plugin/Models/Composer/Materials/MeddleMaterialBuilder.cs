using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public abstract class MeddleMaterialBuilder : MaterialBuilder
{
    public MeddleMaterialBuilder(string name) : base(name)
    {
    }
    
    public abstract MeddleMaterialBuilder Apply();
}
