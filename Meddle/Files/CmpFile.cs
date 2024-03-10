using System.Numerics;
using Penumbra.String.Functions;

namespace Meddle.Plugin.Files;

// "chara/xls/charamake/human.cmp"
public class CmpFile
{
    public readonly uint[] RgbaColors;
    public readonly Vector4[] Colors;

    public CmpFile(byte[] data)
        : this((ReadOnlySpan<byte>)data)
    { }
    
    public unsafe CmpFile(ReadOnlySpan<byte> data)
    {
        // Just copy all the data into an uint array.
        RgbaColors = new uint[data.Length >> 2];
        fixed (byte* ptr1 = data)
        {
            fixed (uint* ptr2 = RgbaColors)
            {
                MemoryUtility.MemCpyUnchecked(ptr2, ptr1, data.Length);
            }
        }
        
        // Convert the uint array into a Vector4 array.
        Colors = new Vector4[RgbaColors.Length];
        for (var i = 0; i < RgbaColors.Length; i++)
        {
            var color = RgbaColors[i];
            Colors[i] = new Vector4(
                (color & 0xFF) / 255f,
                ((color >> 8) & 0xFF) / 255f,
                ((color >> 16) & 0xFF) / 255f,
                ((color >> 24) & 0xFF) / 255f
            );
        }
    }
}