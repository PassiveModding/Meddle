using FFXIVClientStructs.Interop;
using Meddle.Plugin.Xande;

namespace Meddle.Plugin.Models;

public unsafe class Skeleton
{
    public Transform Transform { get; set; }
    public IReadOnlyList<PartialSkeleton> PartialSkeletons { get; set; }
    
    public Skeleton(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton* skeleton)
    {
        Transform = new Transform(skeleton->Transform);
        var partialSkeletons = new List<PartialSkeleton>();
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
            partialSkeletons.Add(new PartialSkeleton(&skeleton->PartialSkeletons[i]));
        
        PartialSkeletons = partialSkeletons;
    }
}
