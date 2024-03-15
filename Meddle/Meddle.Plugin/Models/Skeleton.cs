using FFXIVClientStructs.Interop;
using CSSkeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

namespace Meddle.Plugin.Models;

public unsafe class Skeleton
{
    public Transform Transform { get; set; }
    public IReadOnlyList<PartialSkeleton> PartialSkeletons { get; set; }
    
    public Skeleton(Pointer<CSSkeleton> skeleton) : this(skeleton.Value)
    {
    }
    
    public Skeleton(CSSkeleton* skeleton)
    {
        Transform = new Transform(skeleton->Transform);
        var partialSkeletons = new List<PartialSkeleton>();
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
            partialSkeletons.Add(new PartialSkeleton(&skeleton->PartialSkeletons[i]));
        
        PartialSkeletons = partialSkeletons;
    }
}
