using System.Numerics;
using System.Text.Json.Serialization;
using OtterTex;

namespace Meddle.Utils.Export;

public readonly struct TextureResource(DXGIFormat format, int width, int height, int mipLevels, int arraySize, TexDimension dimension, D3DResourceMiscFlags miscFlags, byte[] data)
{
    public DXGIFormat Format { get; init; } = format;
    public int Width { get; init; } = width;
    public int Height { get; init; } = height;
    
    [JsonIgnore]
    public Vector2 Size => new(Width, Height);
    
    public int MipLevels { get; init; } = mipLevels;
    public int ArraySize { get; init; } = arraySize;
    public TexDimension Dimension { get; init; } = dimension;
    public D3DResourceMiscFlags MiscFlags { get; init; } = miscFlags;
    
    [JsonIgnore]
    public byte[] Data { get; init; } = data;
}
