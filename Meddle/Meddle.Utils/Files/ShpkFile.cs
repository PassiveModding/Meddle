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
    
    
    private byte[] _data;
    public ReadOnlySpan<byte> RawData => _data;
    private int _remainingOffset;
    public ReadOnlySpan<byte> RemainingData => _data[_remainingOffset..];
    public ShpkFile(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        var reader = new SpanBinaryReader(data);
        var magic = reader.ReadUInt32();
        if (magic != ShPkMagic)
            throw new Exception("Invalid SHPK magic");
        
        FileHeader = reader.Read<ShpkHeader>();
        Blobs = data[(int)FileHeader.BlobsOffset..(int)FileHeader.StringsOffset].ToArray();
        Strings = data[(int)FileHeader.StringsOffset..].ToArray();
        
        VertexShaders = ReadShaders(ref reader, (int)FileHeader.VertexShaderCount, Blobs, Shader.ShaderType.Vertex, FileHeader.DxVersion, Strings);
        PixelShaders = ReadShaders(ref reader, (int)FileHeader.PixelShaderCount, Blobs, Shader.ShaderType.Pixel, FileHeader.DxVersion, Strings);
        
        MaterialParams = reader.Read<MaterialParam>((int)FileHeader.MaterialParamCount).ToArray();
        
        Constants = ReadResources(ref reader, (int)FileHeader.ConstantCount, Strings);
        Samplers = ReadResources(ref reader, (int)FileHeader.SamplerCount, Strings);
        Uavs = ReadResources(ref reader, (int)FileHeader.UavCount, Strings);
        
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
        
        _remainingOffset = reader.Position;
    }
    
    public Shader[] ReadShaders(ref SpanBinaryReader r, int count, ReadOnlySpan<byte> blobs, Shader.ShaderType type, uint directXVersion, ReadOnlySpan<byte> strings)
    {
        var shaders = new Shader[count];
        for (var i = 0; i < count; ++i)
        {
            shaders[i] = new Shader(ref r, blobs, type, directXVersion, strings);
        }
        return shaders;
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
        
        public string String;
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
        public byte[] AdditionalHeader;
        public byte[] Blob;
        
        public Shader (ref SpanBinaryReader r, ReadOnlySpan<byte> blobs, ShaderType type, uint directXVersion, ReadOnlySpan<byte> strings)
        {
            Definition = r.Read<ShaderDefinition>();
            if (Definition.Pad != 0)
            {
                throw new NotImplementedException();
            }

            var headerPad = type switch
            {
                ShaderType.Vertex => directXVersion switch
                {
                    Dx9Magic => 4,
                    Dx11Magic => 8,
                    _ => throw new Exception("Invalid DX version")
                },
                _ => 0
            };
            
            var rawBlob = blobs.Slice((int)Definition.BlobOffset, (int)Definition.BlobSize);
            Constants = ReadResources(ref r, Definition.ConstantCount, strings);
            Samplers = ReadResources(ref r, Definition.SamplerCount, strings);
            Uavs = ReadResources(ref r, Definition.UavCount, strings);
            Blob = rawBlob[headerPad..].ToArray();
        }
    }
    
    public static Resource[] ReadResources(ref SpanBinaryReader r, int count, ReadOnlySpan<byte> strings)
    {
        var resources = new Resource[count];
        var stringReader = new SpanBinaryReader(strings);
        for (var i = 0; i < count; ++i)
        {
            resources[i] = new Resource
            {
                Id = r.ReadUInt32(),
                StringOffset = r.ReadUInt32(),
                StringSize = r.ReadUInt32(),
                Slot = r.ReadUInt16(),
                Size = r.ReadUInt16()
            };
            var str = stringReader.ReadString((int)resources[i].StringOffset, (int)resources[i].StringSize);
            resources[i].String = str;
        }
        return resources;
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
        public uint DxVersion;
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
