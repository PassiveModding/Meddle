using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Lumina.Data.Parsing;

namespace Meddle.Plugin.Models;

public unsafe class ShaderPackage
{
    public string Name { get; set; }
    public Dictionary<uint, TextureUsage> TextureLookup { get; set; }
    public FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.ShaderPackage* ShaderPackagePointer { get; set; }

    public ShaderPackage(ShaderPackageResourceHandle* shaderPackage, string name)
    {
        Name = name;

        TextureLookup = new Dictionary<uint, TextureUsage>();
        foreach (var sampler in shaderPackage->ShaderPackage->SamplersSpan)
        {
            if (sampler.Slot != 2)
                continue;
            TextureLookup[sampler.Id] = (TextureUsage)sampler.CRC;
        }
        foreach (var constant in shaderPackage->ShaderPackage->ConstantsSpan)
        {
            if (constant.Slot != 2)
                continue;
            TextureLookup[constant.Id] = (TextureUsage)constant.CRC;
        }
        
        ShaderPackagePointer = shaderPackage->ShaderPackage;
    }
}
