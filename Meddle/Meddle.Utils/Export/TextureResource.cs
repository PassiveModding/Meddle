using System.Numerics;
using System.Text.Json.Serialization;
using OtterTex;

namespace Meddle.Utils.Export;

public readonly struct TextureResource(DXGIFormat format, uint width, uint height, uint mipLevels, uint arraySize, TexDimension dimension, D3DResourceMiscFlags miscFlags, byte[] data)
{
    public DXGIFormat Format { get; init; } = format;
    public uint Width { get; init; } = width;
    public uint Height { get; init; } = height;
    
    [JsonIgnore]
    public Vector2 Size => new(Width, Height);
    
    public uint MipLevels { get; init; } = mipLevels;
    public uint ArraySize { get; init; } = arraySize;
    public TexDimension Dimension { get; init; } = dimension;
    public D3DResourceMiscFlags MiscFlags { get; init; } = miscFlags;
    
    [JsonIgnore]
    public byte[] Data { get; init; } = data;
}
