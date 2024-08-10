using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models.Skeletons;

public class ParsedSkeleton
{
    public unsafe ParsedSkeleton(Pointer<Skeleton> skeleton) : this(skeleton.Value) { }

    public unsafe ParsedSkeleton(Skeleton* skeleton)
    {
        Transform = new Transform(skeleton->Transform);
        var partialSkeletons = new List<ParsedPartialSkeleton>();
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
        {
            try
            {
                partialSkeletons.Add(new ParsedPartialSkeleton(&skeleton->PartialSkeletons[i]));
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to load partial skeleton {i}/{skeleton->PartialSkeletonCount}", e);
            }
        }

        PartialSkeletons = partialSkeletons;
    }

    public Transform Transform { get; }
    public IReadOnlyList<ParsedPartialSkeleton> PartialSkeletons { get; }
}
