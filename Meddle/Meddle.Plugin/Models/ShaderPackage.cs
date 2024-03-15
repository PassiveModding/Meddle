using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Lumina.Data.Parsing;

namespace Meddle.Plugin.Models;

public unsafe class ShaderPackage
{
    public string Name { get; set; }
    public IReadOnlyDictionary<uint, TextureUsage> TextureLookup { get; set; }
    public FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.ShaderPackage* ShaderPackagePointer { get; set; }

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
        ShaderPackagePointer = shaderPackage->ShaderPackage;
    }
}
