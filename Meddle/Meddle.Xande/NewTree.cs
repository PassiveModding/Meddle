using Dalamud.Logging;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Havok;
using FFXIVClientStructs.Interop;
using System.Runtime.InteropServices;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using Lumina.Data.Files;
using System.Drawing;
using Lumina.Data.Parsing;
using System.Numerics;
using LuminaModel = Lumina.Models.Models.Model;
using LuminaMesh = Lumina.Models.Models.Mesh;
using LuminaSubmesh = Lumina.Models.Models.Submesh;
using LuminaShape = Lumina.Models.Models.Shape;
using LuminaShapeMesh = Lumina.Models.Models.ShapeMesh;
using LuminaVertex = Lumina.Models.Models.Vertex;
using LuminaMaterial = Lumina.Models.Materials.Material;
using LuminaTexture = Lumina.Models.Materials.Texture;
using LuminaGameData = Lumina.GameData;
using System.Text.Json.Serialization;
using Meddle.Xande.Utility;
using Serilog;
using SkiaSharp;

namespace Meddle.Xande;

public unsafe class NewTree
{
    public string Name { get; set; }
    public Transform Transform { get; set; }
    public NewSkeleton Skeleton { get; set; }
    public List<NewModel> Models { get; set; }
    public NewAttach Attach { get; set; }

    public ushort? RaceCode { get; set; }

    public List<NewTree>? AttachedChildren { get; set; }

    public NewTree(CSCharacter* character) : this((CharacterBase*)character->GameObject.DrawObject)
    {
        Name = MemoryHelper.ReadStringNullTerminated((nint)character->GameObject.GetName());
        RaceCode = ((Human*)character->GameObject.DrawObject)->RaceSexId;

        AttachedChildren = new();
        foreach (var weaponData in character->DrawData.WeaponDataSpan)
        {
            if (weaponData.Model == null)
                continue;
            var attach = &weaponData.Model->CharacterBase.Attach;
            if (attach->ExecuteType == 0)
                continue;

            AttachedChildren.Add(new(&weaponData.Model->CharacterBase));
        }
    }

    public NewTree(CharacterBase* character)
    {
        var name = stackalloc byte[256];
        name = character->ResolveRootPath(name, 256);
        Name = name != null ? MemoryHelper.ReadString((nint)name, 256) : string.Empty;

        Transform = new(character->DrawObject.Object.Transform);
        Skeleton = new(character->Skeleton);
        Models = new();
        for (var i = 0; i < character->SlotCount; ++i)
        {
            if (character->Models[i] == null)
                continue;
            Models.Add(new(character->Models[i], character->ColorTableTextures + (i * 4)));
        }
        Attach = new(&character->Attach);
    }
}

public unsafe class NewSkeleton
{
    public Transform Transform { get; set; }
    public List<NewPartialSkeleton> PartialSkeletons { get; set; }

    public NewSkeleton(Pointer<Skeleton> skeleton) : this(skeleton.Value)
    {

    }

    public NewSkeleton(Skeleton* skeleton)
    {
        Transform = new(skeleton->Transform);
        PartialSkeletons = new();
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
            PartialSkeletons.Add(new(&skeleton->PartialSkeletons[i]));
    }
}

public unsafe class NewPartialSkeleton
{
    public NewHkSkeleton? HkSkeleton { get; set; }
    public List<NewSkeletonPose> Poses { get; set; }
    public int ConnectedBoneIndex { get; set; }

    public NewPartialSkeleton(Pointer<PartialSkeleton> partialSkeleton) : this(partialSkeleton.Value)
    {

    }

    public NewPartialSkeleton(PartialSkeleton* partialSkeleton)
    {
        if (partialSkeleton->SkeletonResourceHandle != null)
            HkSkeleton = new(partialSkeleton->SkeletonResourceHandle->HavokSkeleton);

        ConnectedBoneIndex = partialSkeleton->ConnectedBoneIndex;

        Poses = new();
        //return;
        for (var i = 0; i < 4; ++i)
        {
            var pose = partialSkeleton->GetHavokPose(i);
            if (pose != null)
            {
                if (pose->Skeleton != partialSkeleton->SkeletonResourceHandle->HavokSkeleton)
                {
                    throw new ArgumentException($"Pose is not the same as the skeleton");
                }
                Poses.Add(new(pose));
            }
        }
    }
}

public unsafe class NewHkSkeleton
{
    public List<string?> BoneNames { get; set; }
    public List<short> BoneParents { get; set; }
    public List<Transform> ReferencePose { get; set; }

    public NewHkSkeleton(Pointer<hkaSkeleton> skeleton) : this(skeleton.Value)
    {

    }

    public NewHkSkeleton(hkaSkeleton* skeleton)
    {
        BoneNames = new();
        BoneParents = new();
        ReferencePose = new();

        for (var i = 0; i < skeleton->Bones.Length; ++i)
        {
            BoneNames.Add(skeleton->Bones[i].Name.String);
            BoneParents.Add(skeleton->ParentIndices[i]);
            ReferencePose.Add(new(skeleton->ReferencePose[i]));
        }
    }
}

public unsafe class NewSkeletonPose
{
    public List<Transform> Pose { get; set; }

    public NewSkeletonPose(Pointer<hkaPose> pose) : this(pose.Value)
    {

    }

    public NewSkeletonPose(hkaPose* pose)
    {
        Pose = new();

        var boneCount = pose->LocalPose.Length;
        for (var i = 0; i < boneCount; ++i)
            Pose.Add(new(pose->LocalPose[i]));
    }
}

public unsafe class NewModel
{
    public string HandlePath { get; set; }
    public ushort? RaceCode { get; set; }

    public List<NewMaterial> Materials { get; set; }

    public List<NewMesh> Meshes { get; set; }
    public List<NewModelShape> Shapes { get; set; }

    public uint ShapesMask { get; set; }
    public uint AttributesMask { get; set; }
    public string[] EnabledShapes { get; set; }
    public string[] EnabledAttributes { get; set; }

    public NewModel(LuminaModel model, LuminaGameData gameData)
    {
        model.Update(gameData);

        HandlePath = model.File?.FilePath.Path ?? "Lumina Model";
        RaceCode = GetRaceCodeFromPath(HandlePath);

        Materials = new();
        foreach (var material in model.Materials)
            Materials.Add(new(material, gameData));

        Meshes = new();
        foreach (var mesh in model.Meshes)
            Meshes.Add(new(mesh));

        Shapes = new();
        foreach (var shape in model.Shapes.Values)
            Shapes.Add(new(shape));

        ShapesMask = 0;
        AttributesMask = 0;

        EnabledShapes = Array.Empty<string>();
        EnabledAttributes = Array.Empty<string>();
    }

    public NewModel(Pointer<Model> model, Pointer<Pointer<Texture>> colorTable) : this(model.Value, (Texture**)colorTable.Value)
    {

    }

    public NewModel(Model* model, Texture** colorTable)
    {
        HandlePath = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
        RaceCode = GetRaceCodeFromPath(HandlePath);

        Materials = new();
        for (var i = 0; i < model->MaterialCount; ++i)
            Materials.Add(new(model->Materials[i], colorTable == null ? null : colorTable[i]));

        Meshes = new();
        Shapes = new();
        LoadMeshesAndShapes(model->ModelResourceHandle);

        ShapesMask = model->EnabledShapeKeyIndexMask;
        AttributesMask = model->EnabledAttributeIndexMask;

        EnabledShapes = model->ModelResourceHandle->Shapes
            .Where(kv => ((1 << kv.Item2) & ShapesMask) != 0)
            .Select(kv => MemoryHelper.ReadStringNullTerminated((nint)kv.Item1.Value))
            .ToArray();
        EnabledAttributes = model->ModelResourceHandle->Attributes
            .Where(kv => ((1 << kv.Item2) & AttributesMask) != 0)
            .Select(kv => MemoryHelper.ReadStringNullTerminated((nint)kv.Item1.Value))
            .ToArray();
    }

    private static ushort? GetRaceCodeFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName[0] != 'c') return null;

        return ushort.Parse(fileName[1..5]);
    }

    private void LoadMeshesAndShapes(ModelResourceHandle* hnd)
    {
        if (hnd->KernelVertexDeclarations == null)
            throw new ArgumentException("No KernelVertexDeclarations exist");

        const int LodIdx = 0;

        var lod = &hnd->Lods[LodIdx];
        var vertexBuffer = DXHelper.ExportVertexBuffer(hnd->VertexBuffersSpan[LodIdx]);
        var indexBuffer = MemoryMarshal.Cast<byte, ushort>(hnd->IndexBuffersSpan[LodIdx].Value->AsSpan());

        var meshRanges = new[] {
            lod->MeshIndex..(lod->MeshIndex + lod->MeshCount),
            lod->WaterMeshIndex..(lod->WaterMeshIndex + lod->WaterMeshCount),
            lod->ShadowMeshIndex..(lod->ShadowMeshIndex + lod->ShadowMeshCount),
            lod->TerrainShadowMeshIndex..(lod->TerrainShadowMeshIndex + lod->TerrainShadowMeshCount),
            lod->VerticalFogMeshIndex..(lod->VerticalFogMeshIndex + lod->VerticalFogMeshCount),
        };
        if (hnd->ExtraLods != null)
        {
            var extraLod = &hnd->ExtraLods[LodIdx];
            meshRanges = meshRanges.Concat(new[]
            {
                extraLod->LightShaftMeshIndex..(extraLod->LightShaftMeshIndex + extraLod->LightShaftMeshCount),
                extraLod->GlassMeshIndex..(extraLod->GlassMeshIndex + extraLod->GlassMeshCount),
                extraLod->MaterialChangeMeshIndex..(extraLod->MaterialChangeMeshIndex + extraLod->MaterialChangeMeshCount),
                extraLod->CrestChangeMeshIndex..(extraLod->CrestChangeMeshIndex + extraLod->CrestChangeMeshCount),
            }).ToArray();
        }

        foreach (var range in meshRanges.AsConsolidated())
        {
            foreach (var meshIdx in range.GetEnumerator())
            {
                var mesh = &hnd->Meshes[meshIdx];
                var meshVertexDecls = hnd->KernelVertexDeclarations[meshIdx];
                var meshVertices = new NewVertex[mesh->VertexCount];
                var meshIndices = indexBuffer.Slice((int)mesh->StartIndex, (int)mesh->IndexCount);

                foreach (var element in meshVertexDecls->ElementsSpan)
                {
                    if (element.Stream == 255)
                        break;

                    var streamIdx = element.Stream;
                    var vertexStreamStride = mesh->VertexBufferStride[streamIdx];
                    var vertexStreamOffset = mesh->VertexBufferOffset[streamIdx];
                    var vertexStreamBuffer = vertexBuffer.AsSpan((int)vertexStreamOffset, vertexStreamStride * mesh->VertexCount);

                    NewVertex.Apply(meshVertices, vertexStreamBuffer, element, vertexStreamStride);
                }

                foreach (var index in meshIndices)
                {
                    if (index < 0)
                        throw new ArgumentException($"Mesh {meshIdx} has index {index}, which is negative");
                    if (index >= meshVertices.Length)
                        throw new ArgumentException($"Mesh {meshIdx} has index {index}, but only {meshVertices.Length} vertices exist");
                }

                if (meshIndices.Length != mesh->IndexCount)
                    throw new ArgumentException($"Mesh {meshIdx} has {meshIndices.Length} indices, but {mesh->IndexCount} were expected");

                Meshes.Add(new(hnd, meshIdx, meshVertices, mesh->StartIndex, meshIndices));
            }
        }
    }
}

public unsafe class NewMaterial
{
    public string HandlePath { get; set; }

    public NewShaderPackage ShaderPackage { get; set; }
    public List<NewTexture> Textures { get; set; }
    [JsonIgnore]
    public Half[]? ColorTable { get; set; }

    [JsonPropertyName("ColorTable")]
    public ushort[]? JsonColorTable => ColorTable?.Select(BitConverter.HalfToUInt16Bits).ToArray();

    public NewMaterial(LuminaMaterial material, LuminaGameData gameData)
    {
        material.Update(gameData);

        HandlePath = material.File?.FilePath.Path ?? "Lumina Material";

        ShaderPackage = new(material.ShaderPack);

        Textures = new();
        uint i = 0;
        foreach(var texture in material.Textures)
        {
            ShaderPackage.TextureLookup[++i] = texture.TextureUsageRaw;
            Textures.Add(new(texture, i, gameData));
        }
    }

    public NewMaterial(Pointer<Material> material, Pointer<Texture> colorTable) : this(material.Value, colorTable.Value)
    {

    }

    public NewMaterial(Material* material, Texture* colorTable)
    {
        HandlePath = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();

        ShaderPackage = new(material->MaterialResourceHandle->ShaderPackageResourceHandle, MemoryHelper.ReadStringNullTerminated((nint)material->MaterialResourceHandle->ShpkName));
        Textures = new();
        for (var i = 0; i < material->MaterialResourceHandle->TextureCount; ++i)
        {
            var handleTexture = &material->MaterialResourceHandle->Textures[i];
            Material.TextureEntry* matEntry = null;
            if (handleTexture->Index1 != 0x1F)
                matEntry = &material->Textures[handleTexture->Index1];
            else if (handleTexture->Index2 != 0x1F)
                matEntry = &material->Textures[handleTexture->Index2];
            else if (handleTexture->Index3 != 0x1F)
                matEntry = &material->Textures[handleTexture->Index3];
            else
            {
                foreach (var tex in material->TexturesSpan)
                {
                    if (tex.Texture == handleTexture->TextureResourceHandle)
                    {
                        matEntry = &tex;
                        break;
                    }
                }
            }
            Textures.Add(new(matEntry, material->MaterialResourceHandle->Strings, handleTexture, ShaderPackage));
        }

        if (colorTable != null)
        {
            var data = DXHelper.ExportTextureResource(colorTable);

            if ((TexFile.TextureFormat)colorTable->TextureFormat != TexFile.TextureFormat.R16G16B16A16F)
                throw new ArgumentException($"Color table is not R16G16B16A16F ({(TexFile.TextureFormat)colorTable->TextureFormat})");
            if (colorTable->Width != 4 || colorTable->Height != 16)
                throw new ArgumentException($"Color table is not 4x16 ({colorTable->Width}x{colorTable->Height})");

            var stridedData = TextureHelper.AdjustStride(data.Stride, (int)colorTable->Width * 8, (int)colorTable->Height, data.Data);

            ColorTable = MemoryMarshal.Cast<byte, Half>(stridedData.AsSpan()).ToArray();
            if (ColorTable.Length != 4 * 16 * 4)
                throw new ArgumentException($"Color table is not 4x16x4 ({ColorTable.Length})");
        }
        else
            Log.Warning($"No color table for {HandlePath}");
    }
}

public unsafe class NewShaderPackage
{
    public string Name { get; set; }
    public Dictionary<uint, TextureUsage> TextureLookup { get; set; }

    public NewShaderPackage(string name)
    {
        Name = name;
        TextureLookup = new();
    }

    public NewShaderPackage(Pointer<ShaderPackageResourceHandle> shaderPackage, string name) : this(shaderPackage.Value, name)
    {

    }

    public NewShaderPackage(ShaderPackageResourceHandle* shaderPackage, string name)
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

public unsafe class NewTexture
{
    public string HandlePath { get; set; }
    public TextureUsage? Usage { get; set; }
    private Texture* KernelTexture { get; set; }
    private TextureResourceHandle* Handle { get; set; }

    public uint? Id { get; set; }
    public uint? SamplerFlags { get; set; }

    [JsonIgnore]
    public TextureHelper.TextureResource Resource { get; }

    public NewTexture(LuminaTexture texture, uint id, LuminaGameData gameData)
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

    public NewTexture(Pointer<Material.TextureEntry> matEntry, Pointer<byte> matHndStrings, Pointer<MaterialResourceHandle.TextureEntry> hndEntry, NewShaderPackage shader) : this(matEntry.Value, matHndStrings.Value, hndEntry.Value, shader)
    {

    }

    public NewTexture(Material.TextureEntry* matEntry, byte* matHndStrings, MaterialResourceHandle.TextureEntry* hndEntry, NewShaderPackage shader)
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

public unsafe class NewMesh
{
    public int MeshIdx { get; set; }
    public ushort MaterialIdx { get; set; }
    //[JsonIgnore]
    public List<NewVertex> Vertices { get; set; }
    [JsonIgnore]
    public List<ushort> Indices { get; set; }
    public List<NewSubMesh> Submeshes { get; set; }
    public List<string>? BoneTable { get; set; }

    public NewMesh(LuminaMesh mesh)
    {
        MeshIdx = mesh.MeshIndex;
        MaterialIdx = mesh.Parent.File!.Meshes[mesh.MeshIndex].MaterialIndex;
        Vertices = new();
        foreach (var vertex in mesh.Vertices)
            Vertices.Add(new(vertex));

        Indices = new();
        foreach (var index in mesh.Indices)
            Indices.Add(index);

        Submeshes = new();
        foreach (var submesh in mesh.Submeshes)
            Submeshes.Add(new(submesh));

        // meshes don't have attributes. lumina is lying to you.

        BoneTable = mesh.BoneTable?.ToList();
    }

    public NewMesh(ModelResourceHandle* hnd, int meshIdx, NewVertex[] vertices, uint meshIndexOffset, ReadOnlySpan<ushort> indices)
    {
        var mesh = &hnd->Meshes[meshIdx];

        MeshIdx = meshIdx;
        MaterialIdx = mesh->MaterialIndex;

        Vertices = vertices.ToList();
        Indices = indices.ToArray().ToList();

        Submeshes = new();

        for (var i = 0; i < mesh->SubMeshCount; ++i)
        {
            var submeshIdx = mesh->SubMeshIndex + i;
            var sm = new NewSubMesh(hnd, submeshIdx);
            // sm.IndexOffset is relative to the model, not the mesh
            sm.IndexOffset -= meshIndexOffset;
            Submeshes.Add(sm);
        }

        if (mesh->BoneTableIndex != 255)
        {
            BoneTable = new();
            var boneTable = &hnd->BoneTables[mesh->BoneTableIndex];
            for (var i = 0; i < boneTable->BoneCount; ++i)
            {
                var namePtr = hnd->StringTable + 8 + hnd->BoneNameOffsets[boneTable->BoneIndex[i]];
                BoneTable.Add(MemoryHelper.ReadStringNullTerminated((nint)namePtr));
            }
        }

        foreach (var index in indices)
        {
            if (index >= vertices.Length)
                throw new ArgumentException($"Mesh {meshIdx} has index {index}, but only {vertices.Length} vertices exist");
        }

        if (indices.Length != mesh->IndexCount)
            throw new ArgumentException($"Mesh {meshIdx} has {indices.Length} indices, but {mesh->IndexCount} were expected");
    }
}

public unsafe struct NewVertex
{
    // VertexType and VertexUsage are as-defined in the .mdl file
    // These get changed into some intermediate other enum
    // When passed onto DX11, the intermediate enum get converted to their real values

    public enum VertexType : byte
    {
        // 0 => 33 => DXGI_FORMAT_R32_FLOAT
        // 1 => 34 => DXGI_FORMAT_R32G32_FLOAT
        Single3 = 2, // => 35 => DXGI_FORMAT_R32G32B32_FLOAT
        Single4 = 3, // => 36 => DXGI_FORMAT_R32G32B32A32_FLOAT
        // 4 Doesn't exist!
        UInt = 5, // => 116 => DXGI_FORMAT_R8G8B8A8_UINT
        // 6 => 82 => DXGI_FORMAT_R16G16_SINT
        // 7 => 84 => DXGI_FORMAT_R16G16B16A16_SINT
        ByteFloat4 = 8, // => 68 => DXGI_FORMAT_R8G8B8A8_UNORM
        // 9 => 18 => DXGI_FORMAT_R16G16_SNORM
        // 10 => 20 => DXGI_FORMAT_R16G16B16A16_SNORM
        // 11 Doesn't exist!
        // 12 Doesn't exist!
        Half2 = 13, // => 50 => DXGI_FORMAT_R16G16_FLOAT
        Half4 = 14 // => 52 => DXGI_FORMAT_R16G16B16A16_FLOAT
        // 15 => 97 => ?? (doesn't exist, so it resolves to DXGI_FORMAT_UNKNOWN)
    }

    public enum VertexUsage : byte
    {
        Position = 0, // => 0 => POSITION0
        BlendWeights = 1, // => 1 => BLENDWEIGHT0
        BlendIndices = 2, // => 7 => BLENDINDICES0
        Normal = 3, // => 2 => NORMAL0
        UV = 4, // => (UsageIndex +) 8 => TEXCOORD0
        Tangent2 = 5, // => 14 => TANGENT0
        Tangent1 = 6, // => 15 => BINORMAL0
        Color = 7, // (UsageIndex +) 3 => COLOR0
    }
    
    public enum KernelVertexType : byte
    {
        DXGI_FORMAT_R16G16_SNORM = 18,
        DXGI_FORMAT_R16G16B16A16_SNORM = 20,
        DXGI_FORMAT_R32_FLOAT = 33,
        DXGI_FORMAT_R32G32_FLOAT = 34,
        DXGI_FORMAT_R32G32B32_FLOAT = 35,
        DXGI_FORMAT_R32G32B32A32_FLOAT = 36,
        DXGI_FORMAT_R16G16_FLOAT = 50,
        DXGI_FORMAT_R16G16B16A16_FLOAT = 52,
        DXGI_FORMAT_R8G8B8A8_UNORM = 68,
        DXGI_FORMAT_R16G16_SINT = 82,
        DXGI_FORMAT_R16G16B16A16_SINT = 84,
        DXGI_FORMAT_R8G8B8A8_UINT = 116
    }

    public enum KernelVertexUsage : byte
    {
        POSITION0,
        BLENDWEIGHT0,
        NORMAL0,
        COLOR0,
        COLOR1,
        FOG0,
        PSIZE0,
        BLENDINDICES0,
        TEXCOORD0,
        TEXCOORD1,
        TEXCOORD2,
        TEXCOORD3,
        TEXCOORD4,
        TEXCOORD5,
        TANGENT0,
        BINORMAL0,
        DEPTH0,
    }

    public Vector4? Position;
    public Vector4? BlendWeights;
    [JsonIgnore]
    public fixed byte BlendIndicesData[4];
    public Vector3? Normal;
    public Vector4? UV;
    public Vector4? Color;
    public Vector4? Tangent2;
    public Vector4? Tangent1;

    [JsonIgnore]
    public Span<byte> BlendIndices
    {
        get
        {
            fixed (byte* buffer = BlendIndicesData)
                return new(buffer, 4);
        }
    }

    [JsonPropertyName("BlendIndices")]
    public int[] BlendIndicesArray => BlendIndices.ToArray().Select(i => (int)i).ToArray();

    public NewVertex(LuminaVertex vertex)
    {
        Position = vertex.Position;
        BlendWeights = vertex.BlendWeights;
        for (var i = 0; i < 4; ++i)
            BlendIndices[i] = vertex.BlendIndices[i];
        Normal = vertex.Normal;
        UV = vertex.UV;
        Color = vertex.Color;
        Tangent2 = vertex.Tangent2;
        Tangent1 = vertex.Tangent1;
    }

    private static class VertexItem
    {
        private static float FromSNorm(short value) =>
            value != short.MinValue ?
                value / 32767.0f :
                -1.0f;

        private static float FromUNorm(byte value) =>
            value / 255.0f;

        public static object GetElement(ReadOnlySpan<byte> buffer, KernelVertexType type)
        {
            fixed (byte* b = buffer)
            {
                var s = (short*)b;
                var h = (Half*)b;
                var f = (float*)b;

                return type switch
                {
                    KernelVertexType.DXGI_FORMAT_R16G16_SNORM => new Vector2(FromSNorm(s[0]), FromSNorm(s[1])),
                    KernelVertexType.DXGI_FORMAT_R16G16B16A16_SNORM => new Vector4(FromSNorm(s[0]), FromSNorm(s[1]), FromSNorm(s[2]), FromSNorm(s[3])),
                    KernelVertexType.DXGI_FORMAT_R32_FLOAT => f[0],
                    KernelVertexType.DXGI_FORMAT_R32G32_FLOAT => new Vector2(f[0], f[1]),
                    KernelVertexType.DXGI_FORMAT_R32G32B32_FLOAT => new Vector3(f[0], f[1], f[2]),
                    KernelVertexType.DXGI_FORMAT_R32G32B32A32_FLOAT => new Vector4(f[0], f[1], f[2], f[3]),
                    KernelVertexType.DXGI_FORMAT_R16G16_FLOAT => new Vector2((float)h[0], (float)h[1]),
                    KernelVertexType.DXGI_FORMAT_R16G16B16A16_FLOAT => new Vector4((float)h[0], (float)h[1], (float)h[2], (float)h[3]),
                    KernelVertexType.DXGI_FORMAT_R8G8B8A8_UNORM => new Vector4(FromUNorm(b[0]), FromUNorm(b[1]), FromUNorm(b[2]), FromUNorm(b[3])),
                    KernelVertexType.DXGI_FORMAT_R16G16_SINT => new Vector2(s[0], s[1]),
                    KernelVertexType.DXGI_FORMAT_R16G16B16A16_SINT => new Vector4(s[0], s[1], s[2], s[3]),
                    KernelVertexType.DXGI_FORMAT_R8G8B8A8_UINT => new Vector4(b[0], b[1], b[2], b[3]),
                    _ => throw new ArgumentException($"Unknown type {type}"),
                };
            }
        }

        public static T ConvertTo<T>(object value) where T : struct
        {
            if (value is T t)
                return t;
            if (value is Vector2 v2)
            {
                if (typeof(T) == typeof(Vector3))
                    return (T)(object)new Vector3(v2, 0);
                if (typeof(T) == typeof(Vector4))
                    return (T)(object)new Vector4(v2, 0, 0);
            }
            if (value is Vector3 v3)
            {
                if (typeof(T) == typeof(Vector2))
                    return (T)(object)new Vector2(v3.X, v3.Y);
                if (typeof(T) == typeof(Vector4))
                    return (T)(object)new Vector4(v3, 0);
            }
            if (value is Vector4 v4)
            {
                if (typeof(T) == typeof(Vector2))
                    return (T)(object)new Vector2(v4.X, v4.Y);
                if (typeof(T) == typeof(Vector3))
                    return (T)(object)new Vector3(v4.X, v4.Y, v4.Z);
            }
            throw new ArgumentException($"Cannot convert {value} to {typeof(T)}");
        }
    }

    public static void Apply(NewVertex[] vertices, ReadOnlySpan<byte> buffer, FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.VertexElement element, byte stride)
    {
        for (var i = 0; i < vertices.Length; ++i)
        {
            ref var vert = ref vertices[i];
            var buf = buffer.Slice(i * stride, stride);

            var item = VertexItem.GetElement(buf[element.Offset..], (KernelVertexType)element.Type);

            switch ((KernelVertexUsage)element.Usage)
            {
                case KernelVertexUsage.POSITION0:
                    vert.Position = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.BLENDWEIGHT0:
                    vert.BlendWeights = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.BLENDINDICES0:
                    var itemVector = VertexItem.ConvertTo<Vector4>(item);
                    for (var j = 0; j < 4; ++j)
                        vert.BlendIndices[j] = (byte)itemVector[j];
                    break;
                case KernelVertexUsage.NORMAL0:
                    vert.Normal = VertexItem.ConvertTo<Vector3>(item);
                    break;
                case KernelVertexUsage.TEXCOORD0:
                    vert.UV = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.COLOR0:
                    vert.Color = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.TANGENT0:
                    vert.Tangent2 = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.BINORMAL0:
                    vert.Tangent1 = VertexItem.ConvertTo<Vector4>(item);
                    break;
                default:
                    PluginLog.Error($"Skipped usage {(KernelVertexUsage)element.Usage} [{(KernelVertexType)element.Type}] = {item}");
                    break;
            }
        }
    }
}

public unsafe class NewSubMesh
{
    public uint IndexOffset { get; set; }
    public uint IndexCount { get; set; }
    public List<string> Attributes { get; set; }

    public NewSubMesh(LuminaSubmesh mesh)
    {
        IndexOffset = mesh.IndexOffset;
        IndexCount = mesh.IndexNum;
        Attributes = new();
        foreach (var attribute in mesh.Attributes)
            Attributes.Add(attribute);
    }

    public NewSubMesh(ModelResourceHandle* handle, int idx)
    {
        var submesh = &handle->Submeshes[idx];
        IndexOffset = submesh->IndexOffset;
        IndexCount = submesh->IndexCount;

        Attributes = new();
        foreach (var (namePtr, id) in handle->Attributes)
        {
            if ((submesh->AttributeIndexMask & (1u << id)) != 0)
                Attributes.Add(MemoryHelper.ReadStringNullTerminated((nint)namePtr.Value));
        }
    }
}

public unsafe class NewModelShape
{
    public string Name { get; set; }
    [JsonIgnore]
    public List<NewModelShapeMesh> Meshes { get; set; }

    public NewModelShape(LuminaShape shape)
    {
        Name = shape.Name;
        Meshes = new();
        foreach (var mesh in shape.Meshes)
            Meshes.Add(new(mesh));
    }
}

public unsafe class NewModelShapeMesh
{
    public NewMesh AssociatedMesh { get; set; }
    public List<(ushort BaseIndicesIndex, ushort ReplacingVertexIndex)> Values { get; set; }

    public NewModelShapeMesh(LuminaShapeMesh shapeMesh)
    {
        // TODO: associate mesh by id or by actual reference!
        AssociatedMesh = new(shapeMesh.AssociatedMesh);
        Values = new();
        foreach (var value in shapeMesh.Values)
            Values.Add(value);
    }
}

public unsafe class NewAttach
{
    public int ExecuteType { get; set; }

    public Transform Transform { get; set; }
    public byte PartialSkeletonIdx { get; set; }
    public ushort BoneIdx { get; set; }

    public NewAttach()
    {

    }

    public NewAttach(Attach* attach)
    {
        ExecuteType = attach->ExecuteType;
        if (ExecuteType == 0)
            return;

        if (attach->ExecuteType != 4)
        {
            PluginLog.Error($"Unsupported ExecuteType {attach->ExecuteType}");
            return;
        }

        var att = attach->SkeletonBoneAttachments[0];

        Transform = new(att.ChildTransform);

        PartialSkeletonIdx = att.SkeletonIdx;
        BoneIdx = att.BoneIdx;
    }
}
