namespace Meddle.Utils.Files;

public class ShpkFile
{
    public const uint ShPkMagic = 0x6B506853u; // bytes of ShPk
    private const uint Dx9Magic  = 0x00395844u; // bytes of DX9\0
    private const uint Dx11Magic = 0x31315844u; // bytes of DX11
    
    public ShpkFile(byte[] data) : this(new Span<byte>(data))
    {
    }
    
    public ShpkHeader FileHeader;
    public byte[] Blobs;
    public byte[] Strings;
    public Shader[] VertexShaders;
    public Shader[] PixelShaders;
    public MaterialParam[] MaterialParams;
    public Resource[] Constants;
    public Resource[] Samplers;
    public Resource[] Uavs;
    public Key[] SystemKeys;
    public Key[] SceneKeys;
    public Key[] MaterialKeys;
    public Key[] SubViewKeys;
    public Node[] Nodes;
    
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
        Blobs = data[(int)FileHeader.BlobsOffset..(int)FileHeader.StringsOffset].ToArray();
        Strings = data[(int)FileHeader.StringsOffset..].ToArray();
        
        VertexShaders = new Shader[FileHeader.VertexShaderCount];
        PixelShaders = new Shader[FileHeader.PixelShaderCount];

        for (var i = 0; i < FileHeader.VertexShaderCount; i++)
        {
            VertexShaders[i] = ReadShader(ref reader, Blobs);
        }

        for (var i = 0; i < FileHeader.PixelShaderCount; i++)
        {
            PixelShaders[i] = ReadShader(ref reader, Blobs);
        }

        MaterialParams = reader.Read<MaterialParam>((int)FileHeader.MaterialParamCount).ToArray();
        
        Constants = reader.Read<Resource>((int)FileHeader.ConstantCount).ToArray();
        Samplers = reader.Read<Resource>((int)FileHeader.SamplerCount).ToArray();
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
            var systemKeys = reader.Read<uint>(SystemKeys.Length).ToArray();
            var sceneKeys = reader.Read<uint>(SceneKeys.Length).ToArray();
            var materialKeys = reader.Read<uint>(MaterialKeys.Length).ToArray();
            var subViewKeys = reader.Read<uint>(SubViewKeys.Length).ToArray();
            var passes = reader.Read<Pass>((int)passCount).ToArray();
        
            Nodes[i] = new Node
            {
                Selector = selector,
                PassIndices = passIndices,
                SystemKeys = systemKeys,
                SceneKeys = sceneKeys,
                MaterialKeys = materialKeys,
                SubViewKeys = subViewKeys,
                Passes = passes
            };
        }
        
        remainingOffset = reader.Position;
    }

    public Shader ReadShader(ref SpanBinaryReader r, ReadOnlySpan<byte> blob)
    {
        var definition = r.Read<Shader.ShaderDefinition>();
        if (definition.Pad != 0)
        {
            throw new NotImplementedException();
        }
        
        var constants = r.Read<Resource>(definition.ConstantCount).ToArray();
        var samplers = r.Read<Resource>(definition.SamplerCount).ToArray();
        var uavs = r.Read<Resource>(definition.UavCount).ToArray();

        return new Shader
        {
            Definition = definition,
            Constants = constants,
            Samplers = samplers,
            Uavs = uavs
        };
    }
    
    public struct Pass
    {
        public uint Id;
        public uint VertexShader;
        public uint PixelShader;
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
        public uint[] SystemKeys;
        public uint[] SceneKeys;
        public uint[] MaterialKeys;
        public uint[] SubViewKeys;
        public Pass[] Passes;
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
            public ushort Pad;
        }
        
        public ShaderDefinition Definition;
        public Resource[] Constants;
        public Resource[] Samplers;
        public Resource[] Uavs;
    }

    public struct MaterialParam
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
        public uint MaterialParamCount;
        public uint ConstantCount;
        public uint SamplerCount;
        public uint UavCount;
        public uint SystemKeyCount;
        public uint SceneKeyCount;
        public uint MaterialKeyCount;
        public uint NodeCount;
        public uint NodeAliasCount;
    }
}
