using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Lumina.Data.Parsing;

namespace Meddle.Plugin.Xande.Models;

public unsafe class ShaderPackage
{
    public string Name { get; set; }
    public Dictionary<uint, TextureUsage> TextureLookup { get; set; }

    public ShaderPackage(string name)
    {
        Name = name;
        TextureLookup = new();
    }

    public ShaderPackage(Pointer<ShaderPackageResourceHandle> shaderPackage, string name) : this(shaderPackage.Value, name)
    {

    }

    public ShaderPackage(ShaderPackageResourceHandle* shaderPackage, string name)
    {
        Name = name;

        TextureLookup = new();
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
    }
}
