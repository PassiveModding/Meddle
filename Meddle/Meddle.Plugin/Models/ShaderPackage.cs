using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Lumina.Data.Parsing;

namespace Meddle.Plugin.Models;

public unsafe class ShaderPackage
{
    public string Name { get; }
    public IReadOnlyDictionary<uint, TextureUsage> TextureLookup { get; }

    public ShaderPackage(Pointer<ShaderPackageResourceHandle> shaderPackage, string name) : this(shaderPackage.Value, name)
    {

    }
    
    public ShaderPackage(ShaderPackageResourceHandle* shaderPackage, string name)
    {
        Name = name;

        var textureUsages = new Dictionary<uint, TextureUsage>();
        foreach (var sampler in shaderPackage->ShaderPackage->SamplersSpan)
        {
            if (sampler.Slot != 2)
                continue;
            textureUsages[sampler.Id] = (TextureUsage)sampler.CRC;
        }
        foreach (var constant in shaderPackage->ShaderPackage->ConstantsSpan)
        {
            if (constant.Slot != 2)
                continue;
            textureUsages[constant.Id] = (TextureUsage)constant.CRC;
        }
        
        TextureLookup = textureUsages;
    }
}
