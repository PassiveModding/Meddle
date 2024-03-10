using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Xande.Models;

public unsafe class Skeleton
{
    public Transform Transform { get; set; }
    public List<PartialSkeleton> PartialSkeletons { get; set; }

    public Skeleton(Pointer<FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton> skeleton) : this(skeleton.Value)
    {

    }

    public Skeleton(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton* skeleton)
    {
        Transform = new(skeleton->Transform);
        PartialSkeletons = new();
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
            PartialSkeletons.Add(new(&skeleton->PartialSkeletons[i]));
    }
}
