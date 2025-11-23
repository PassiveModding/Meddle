using System.Numerics;
using System.Text.Json.Serialization;
using BCnEncoder.Shared.ImageFiles;

namespace Meddle.Utils.Export;

public readonly struct TextureResource(DxgiFormat format, uint width, uint height, uint mipLevels, uint arraySize, D3D10ResourceDimension dimension, bool isCube, byte[] data)
{
    public DxgiFormat Format { get; init; } = format;
    public uint Width { get; init; } = width;
    public uint Height { get; init; } = height;
    
    [JsonIgnore]
    public Vector2 Size => new(Width, Height);
    
    public uint MipLevels { get; init; } = mipLevels;
    public uint ArraySize { get; init; } = arraySize;
    public D3D10ResourceDimension Dimension { get; init; } = dimension;
    
    public bool IsCube { get; } = isCube;

    [JsonIgnore]
    public byte[] Data { get; init; } = data;
}
