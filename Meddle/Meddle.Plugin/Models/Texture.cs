using System.Text.Json.Serialization;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Lumina.Data.Parsing;
using Meddle.Plugin.Utility;

namespace Meddle.Plugin.Models;

public unsafe class Texture
{
    public string HandlePath { get; set; }
    public TextureUsage? Usage { get; set; }
    private FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture* KernelTexture { get; set; }
    private TextureResourceHandle* Handle { get; set; }

    public uint? Id { get; set; }
    public uint? SamplerFlags { get; set; }

    [JsonIgnore]
    public TextureHelper.TextureResource Resource { get; }

    public Texture(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material.TextureEntry* matEntry, byte* matHndStrings, MaterialResourceHandle.TextureEntry* hndEntry, ShaderPackage shader)
    {
        HandlePath = MemoryHelper.ReadStringNullTerminated((nint)matHndStrings + hndEntry->PathOffset);
        KernelTexture = hndEntry->TextureResourceHandle->Texture;
        Handle = hndEntry->TextureResourceHandle;

        if (matEntry != null)
        {
            Id = matEntry->Id;
            SamplerFlags = matEntry->SamplerFlags;
            if (shader.TextureLookup.TryGetValue(Id.Value, out var usage))
                Usage = usage;
        }

        Resource = DXHelper.ExportTextureResource(KernelTexture);
    }
}
