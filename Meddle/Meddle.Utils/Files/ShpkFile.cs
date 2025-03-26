using Microsoft.Extensions.Logging;

namespace Meddle.Utils.Files;

public class ShpkFile
{
    public const uint ShPkMagic = 0x6B506853u; // bytes of ShPk
    private const uint Dx9Magic  = 0x00395844u; // bytes of DX9\0
    private const uint Dx11Magic = 0x31315844u; // bytes of DX11

    private const uint Shpk13_1 = 0x0D01;
    
    public const uint MaterialParamsConstantId = 0x64D12851u; // g_MaterialParameter is a cbuffer filled from the ad hoc section of the mtrl
    
    public ShpkFile(byte[] data) : this(new Span<byte>(data))
    {
    }
    
    public ShpkHeader FileHeader;

    public uint? Unk131A;
    public uint? Unk131B;
    public uint? Unk131C;
    //public byte[] Blobs;
    //public byte[] Strings;
    public Shader[] VertexShaders;
    public Shader[] PixelShaders;
    public MaterialParam[] MaterialParams;
    public float[] MaterialParamDefaults;
    public Resource[] Constants;
    public Resource[] Samplers;
    public Resource[] Textures;
    public Resource[] Uavs;
    public Key[] SystemKeys;
    public Key[] SceneKeys;
    public Key[] MaterialKeys;
    public Key[] SubViewKeys;
    public Node[] Nodes;
    public NodeAlias[] NodeAliases;
    
    public enum DxVersion : uint
    {
        Dx9 = Dx9Magic,
        Dx11 = Dx11Magic
    }
    
    
    private readonly byte[] data;
    public ReadOnlySpan<byte> RawData => data;
    private readonly int remainingOffset;
    public ReadOnlySpan<byte> RemainingData => data.AsSpan()[remainingOffset..];
    public ShpkFile(ReadOnlySpan<byte> data)
    {
        this.data = data.ToArray();
        var reader = new SpanBinaryReader(data);
        var magic = reader.ReadUInt32();
        if (magic != ShPkMagic)
            throw new Exception("Invalid SHPK magic");
        
        FileHeader = reader.Read<ShpkHeader>();
        
        if (data.Length != FileHeader.Length)
            throw new Exception("Invalid SHPK length");

        if (FileHeader.Version >= Shpk13_1)
        {
            Unk131A = reader.ReadUInt32();
            Unk131B = reader.ReadUInt32();
            Unk131C = reader.ReadUInt32();
        }
        
        // var blobs = data[(int)FileHeader.BlobsOffset..(int)FileHeader.StringsOffset];
        // var strings = data[(int)FileHeader.StringsOffset..];
        
        VertexShaders = new Shader[FileHeader.VertexShaderCount];
        PixelShaders = new Shader[FileHeader.PixelShaderCount];

        for (var i = 0; i < FileHeader.VertexShaderCount; i++)
        {
            VertexShaders[i] = ReadShader(ref reader, Shader.ShaderType.Vertex);
        }

        for (var i = 0; i < FileHeader.PixelShaderCount; i++)
        {
            PixelShaders[i] = ReadShader(ref reader, Shader.ShaderType.Pixel);
        }

        MaterialParams = reader.Read<MaterialParam>((int)FileHeader.MaterialParamCount).ToArray();
        if (FileHeader.HasMatParamDefaults != 0)
        {
            var size = FileHeader.MaterialParamsSize >> 2;
            MaterialParamDefaults = reader.Read<float>((int)size).ToArray();
        }
        else
        {
            MaterialParamDefaults = [];
        }
        
        Constants = reader.Read<Resource>((int)FileHeader.ConstantCount).ToArray();
        Samplers = reader.Read<Resource>((int)FileHeader.SamplerCount).ToArray();
        Textures = reader.Read<Resource>((int)FileHeader.TextureCount).ToArray();
        Uavs = reader.Read<Resource>((int)FileHeader.UavCount).ToArray();
        
        SystemKeys = reader.Read<Key>((int)FileHeader.SystemKeyCount).ToArray();
        SceneKeys = reader.Read<Key>((int)FileHeader.SceneKeyCount).ToArray();
        MaterialKeys = reader.Read<Key>((int)FileHeader.MaterialKeyCount).ToArray();

        SubViewKeys =
        [
            new Key {Id = 1, DefaultValue = reader.ReadUInt32()},
            new Key {Id = 2, DefaultValue = reader.ReadUInt32()}
        ];
        

        Nodes = new Node[FileHeader.NodeCount];

        for (var i = 0; i < FileHeader.NodeCount; i++)
        {
            var selector = reader.ReadUInt32();
            var passCount = reader.ReadUInt32();
            var passIndices = reader.Read<byte>(16).ToArray();
            uint? unk131E = null;
            uint? unk131F = null;
            if (FileHeader.Version >= Shpk13_1)
            {
                unk131E = reader.ReadUInt32();
                unk131F = reader.ReadUInt32();
            }
            
            var systemKeys = reader.Read<uint>(SystemKeys.Length).ToArray();
            var sceneKeys = reader.Read<uint>(SceneKeys.Length).ToArray();
            var materialKeys = reader.Read<uint>(MaterialKeys.Length).ToArray();
            var subViewKeys = reader.Read<uint>(SubViewKeys.Length).ToArray();
            Pass[] passes = new Pass[passCount];
            for (var j = 0; j < passCount; j++)
            {
                passes[j] = ReadPass(ref reader);
            }
        
            Nodes[i] = new Node
            {
                Selector = selector,
                PassIndices = passIndices,
                Unk131E = unk131E,
                Unk131F = unk131F,
                SystemKeys = systemKeys,
                SceneKeys = sceneKeys,
                MaterialKeys = materialKeys,
                SubViewKeys = subViewKeys,
                Passes = passes
            };
        }
        
        NodeAliases = reader.Read<NodeAlias>((int)FileHeader.NodeAliasCount).ToArray();
        
        remainingOffset = reader.Position;
    }
    
    public Pass ReadPass(ref SpanBinaryReader r)
    {
        var id = r.ReadUInt32();
        var vertexShader = r.ReadUInt32();
        var pixelShader = r.ReadUInt32();
        uint? unk131G = null;
        uint? unk131H = null;
        uint? unk131I = null;
        if (FileHeader.Version >= Shpk13_1)
        {
            unk131G = r.ReadUInt32();
            unk131H = r.ReadUInt32();
            unk131I = r.ReadUInt32();
        }
        
        return new Pass
        {
            Id = id,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            Unk131G = unk131G,
            Unk131H = unk131H,
            Unk131I = unk131I
        };
    }

    public Shader ReadShader(ref SpanBinaryReader r, Shader.ShaderType type)
    {
        var definition = r.Read<Shader.ShaderDefinition>();
        uint? unk131d = null;
        if (FileHeader.Version >= Shpk13_1)
        {
            unk131d = r.ReadUInt32();
        }

        var constants = r.Read<Resource>(definition.ConstantCount).ToArray();
        var samplers = r.Read<Resource>(definition.SamplerCount).ToArray();
        var uavs = r.Read<Resource>(definition.UavCount).ToArray();
        var textures = r.Read<Resource>(definition.TextureCount).ToArray();
        
        var fullBlob = data.AsSpan()[(int)FileHeader.BlobsOffset..(int)FileHeader.StringsOffset];
        var rawBlob = fullBlob.Slice((int)definition.BlobOffset, (int)definition.BlobSize);

        var size = definition.BlobSize;
        var additionalHeaderSize = 0;
        if (type == Shader.ShaderType.Vertex && size >= 4)
        {
            size -= sizeof(uint);
            additionalHeaderSize += sizeof(uint);
        }

        if (type == Shader.ShaderType.Vertex && FileHeader.DxVersion == DxVersion.Dx11 && size >= 4)
        {
            size -= sizeof(uint);
            additionalHeaderSize += sizeof(uint);
        }
        
        var additionalHeader = rawBlob[..additionalHeaderSize];
        var shaderBlob = rawBlob[additionalHeaderSize..];

        return new Shader
        {
            Definition = definition,
            Unk131D = unk131d,
            Constants = constants,
            Samplers = samplers,
            Uavs = uavs,
            Textures = textures,
            AdditionalHeader = additionalHeader.ToArray(),
            Blob = shaderBlob.ToArray()
        };
    }
    
    public struct Pass
    {
        public uint Id;
        public uint VertexShader;
        public uint PixelShader;
        public uint? Unk131G;
        public uint? Unk131H;
        public uint? Unk131I;
    }

    public struct Key
    {
        public uint   Id;
        public uint   DefaultValue;
    }

    public struct Node
    {
        public uint   Selector;
        public byte[] PassIndices;
        public uint?  Unk131E;
        public uint?  Unk131F;
        public uint[] SystemKeys;
        public uint[] SceneKeys;
        public uint[] MaterialKeys;
        public uint[] SubViewKeys;
        public Pass[] Passes;
    }
    
    public struct NodeAlias
    {
        public uint Selector;
        public uint Alias;
    }
    
    public struct Resource
    {
        public uint Id;
        public uint StringOffset;
        public uint StringSize;
        public ushort Slot;
        public ushort Size;
    }
    
    public struct Shader
    {
        public enum ShaderType
        {
            Vertex,
            Pixel
        }
        
        public struct ShaderDefinition
        {
            public uint BlobOffset;
            public uint BlobSize;
            public ushort ConstantCount;
            public ushort SamplerCount;
            public ushort UavCount;
            public ushort TextureCount;
        }
        
        public ShaderDefinition Definition;
        public uint? Unk131D;
        public Resource[] Constants;
        public Resource[] Samplers;
        public Resource[] Uavs;
        public Resource[] Textures;
        public byte[] AdditionalHeader;
        public byte[] Blob;
    }

    public record struct MaterialParam
    {
        public uint   Id;
        public ushort ByteOffset;
        public ushort ByteSize;
    }

    public struct ShpkHeader
    {
        public uint Version;
        public DxVersion DxVersion;
        public uint Length;
        public uint BlobsOffset;
        public uint StringsOffset;
        public uint VertexShaderCount;
        public uint PixelShaderCount;
        public uint MaterialParamsSize;
        public ushort MaterialParamCount;
        public ushort HasMatParamDefaults;
        public ushort ConstantCount;
        public ushort Unk1;
        public ushort SamplerCount;
        public ushort TextureCount;
        public ushort UavCount;
        public ushort Unk2;
        public uint SystemKeyCount;
        public uint SceneKeyCount;
        public uint MaterialKeyCount;
        public uint NodeCount;
        public uint NodeAliasCount;
    }
}
