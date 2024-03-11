using System.Text.Json.Serialization;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Xande;

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

    public Texture(Lumina.Models.Materials.Texture texture, uint id, GameData gameData)
    {
        HandlePath = texture.TexturePath;
        Usage = texture.TextureUsageRaw;
        KernelTexture = null;
        Handle = null;

        Id = id;
        SamplerFlags = 0;

        var f = gameData.GetFile<TexFile>(HandlePath) ?? throw new ArgumentException($"Texture {HandlePath} not found");
        Resource = TextureHelper.FromTexFile(f);
    }

    public Texture(Pointer<FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material.TextureEntry> matEntry, Pointer<byte> matHndStrings, Pointer<MaterialResourceHandle.TextureEntry> hndEntry, ShaderPackage shader) : this(matEntry.Value, matHndStrings.Value, hndEntry.Value, shader)
    {

    }

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
