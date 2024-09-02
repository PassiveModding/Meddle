using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Memory;

namespace Meddle.Plugin.Models.Composer;

public abstract class InstanceMaterialBuilder : XivMaterialBuilder
{
    protected readonly Func<string, byte[]?> LookupFunc;
    protected readonly Func<SKTexture, string, ImageBuilder> CacheFunc;

    public InstanceMaterialBuilder(string name, string shpk,  Func<string, byte[]?> lookupFunc, Func<SKTexture, string, ImageBuilder> cacheFunc) : base(name, shpk)
    {
        this.LookupFunc = lookupFunc;
        this.CacheFunc = cacheFunc;
    }
}
