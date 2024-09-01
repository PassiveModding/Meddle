using System.Text;
using Meddle.Utils.Files;

namespace Meddle.Utils.Export;

public unsafe class ShaderPackage
{
    public string Name { get; }
    public Dictionary<uint, TextureUsage> TextureLookup { get; }
    public Dictionary<MaterialConstant, float[]> MaterialConstants { get; }
    public Dictionary<uint, string>? ResourceKeys { get; }

    public ShaderPackage(ShpkFile file, string name)
    {
        Name = name;
        
        var textureUsages = new Dictionary<uint, TextureUsage>();
        var resourceKeys = new Dictionary<uint, string>();
        var stringReader = new SpanBinaryReader(file.RawData[(int)file.FileHeader.StringsOffset..]);
        foreach (var sampler in file.Samplers)
        {
            if (sampler.Slot != 2)
                continue;
            
            var resName = stringReader.ReadString((int)sampler.StringOffset);
            // compute crc
            var crc = (TextureUsage)Crc32.GetHash(resName);
            textureUsages[sampler.Id] = crc;
            resourceKeys[sampler.Id] = resName;
        }
        
        foreach (var constant in file.Constants)
        {
            if (constant.Slot != 2)
                continue;
            var resName = stringReader.ReadString((int)constant.StringOffset);  
            var crc = (TextureUsage)Crc32.GetHash(resName);
            textureUsages[constant.Id] = crc;
            resourceKeys[constant.Id] = resName;
        }
        
        foreach (var texture in file.Textures)
        {
            if (texture.Slot != 2)
                continue;
            var resName = stringReader.ReadString((int)texture.StringOffset);
            var crc = (TextureUsage)Crc32.GetHash(resName);
            textureUsages[texture.Id] = crc;
            resourceKeys[texture.Id] = resName;
        }
        
        foreach (var uav in file.Uavs)
        {
            if (uav.Slot != 2)
                continue;
            var resName = stringReader.ReadString((int)uav.StringOffset);
            var crc = (TextureUsage)Crc32.GetHash(resName);
            textureUsages[uav.Id] = crc;
            resourceKeys[uav.Id] = resName;
        }
        
        var materialConstantDict = new Dictionary<MaterialConstant, float[]>();
        var orderedMaterialParams = file.MaterialParams.Select((x, idx) => (x, idx)).OrderBy(x => x.x.ByteOffset).ToArray();
        foreach (var (materialParam, i) in orderedMaterialParams)
        {
            // get defaults from byteoffset -> byteoffset + bytesize
            var defaults = file.MaterialParamDefaults
                               .Skip(materialParam.ByteOffset / 4)
                               .Take(materialParam.ByteSize / 4).ToArray();
            var defaultCopy = new float[defaults.Length];
            Array.Copy(defaults, defaultCopy, defaults.Length);
            materialConstantDict[(MaterialConstant)materialParam.Id] = defaultCopy;
        }
        
        MaterialConstants = materialConstantDict;
        TextureLookup = textureUsages;
        ResourceKeys = resourceKeys;
    }
}

public class Crc32
{
    private static uint[]? CrcTable;
    private const int Width = 32;
    private const uint Poly = 0x04C11DB7;
    private const int MsbMask = 0x01 << (Width - 1);
    private static uint[] GetCrcTable()
    {
        if (CrcTable != null)
        {
            return CrcTable;
        }

        var table = new uint[256];
        for (var i = 0; i < 256; i++)
        {
            var currentByte = (uint) (i << (Width - 8));
            for (var bit = 0; bit < 8; bit++)
            {
                if ((currentByte & MsbMask) != 0)
                {
                    currentByte = (currentByte << 1) ^ Poly;
                }
                else
                {
                    currentByte <<= 1;
                }
            }
        
            table[i] = currentByte;
        }
    
        CrcTable = table;
        return table;
    }

    private static byte Reflect8(uint val)
    {
        var resByte = 0U;
        for (var i = 0; i < 8; i++)
        {
            if ((val & (1U << i)) != 0)
            {
                resByte |= (1U << (7 - i));
            }
        }

        return (byte) resByte;
    }

    private static uint Reflect32(uint val)
    {
        var res = 0U;
        for (var i = 0; i < 32; i++)
        {
            if ((val & (1U << i)) != 0)
            {
                res |= (1U << (31 - i));
            }
        }

        return res;
    }

    public static uint GetHash(string value)
    {
        // reflect input
        var data = Encoding.UTF8.GetBytes(value).AsSpan();

        var crcLocal = 0U;
        var table = GetCrcTable();
        foreach (var t in data)
        {
            crcLocal ^= (uint)Reflect8(t) << (Width - 8);
            crcLocal = (crcLocal << 8) ^ table[crcLocal >> (Width - 8)];
        }
    
        // reverse crc
        crcLocal = Reflect32(crcLocal);
    
        return crcLocal ^ 0U;
    }
}
