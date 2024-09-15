using System.Numerics;
using System.Runtime.CompilerServices;
using Meddle.Utils;

namespace Meddle.Plugin.Models.Composer;

public static class Partitioner
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Iterate(this SKTexture texture, Action<int, int> partitionAction)
    {
        Iterate(texture.Width, texture.Height, partitionAction);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Iterate(this Vector2 size, Action<int, int> partitionAction)
    {
        Iterate((int)size.X, (int)size.Y, partitionAction);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Iterate(int width, int height, Action<int, int> partitionAction)
    {
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                partitionAction(x, y);
            }
        });
    }
}
