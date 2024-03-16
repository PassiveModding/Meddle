using System.Text.Json.Serialization;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Lumina.Data.Parsing;
using Meddle.Plugin.Utility;
using CSTexture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using CSTextureEntry = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material.TextureEntry;

namespace Meddle.Plugin.Models;

public unsafe class Texture
{
    public string HandlePath { get; }
    public TextureUsage? Usage { get; }
    public uint? Id { get; }
    public uint? SamplerFlags { get; }
    
    [JsonIgnore]
    public TextureHelper.TextureResource Resource { get; }
    
    public Texture(Pointer<CSTextureEntry> matEntry, Pointer<byte> matHndStrings, 
                   Pointer<MaterialResourceHandle.TextureEntry> hndEntry, ShaderPackage shader) : 
        this(matEntry.Value, matHndStrings.Value, hndEntry.Value, shader)
    {

    }

    public Texture(CSTextureEntry* matEntry, byte* matHndStrings, MaterialResourceHandle.TextureEntry* hndEntry, ShaderPackage shader)
    {
        HandlePath = MemoryHelper.ReadStringNullTerminated((nint)matHndStrings + hndEntry->PathOffset);

        if (matEntry != null)
        {
            Id = matEntry->Id;
            SamplerFlags = matEntry->SamplerFlags;
            if (shader.TextureLookup.TryGetValue(Id.Value, out var usage))
                Usage = usage;
        }

        Resource = DXHelper.ExportTextureResource(hndEntry->TextureResourceHandle->Texture);
    }
}
