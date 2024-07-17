namespace Meddle.Utils.Skeletons.Havok.Models;

public class HavokPartialSkeleton
{
    /// <summary>The ID of the skeleton.</summary>
    public int Id { get; init; }

    /// <summary>The reference pose of the skeleton (also known as the "resting" or "base" pose).</summary>
    public float[][] ReferencePose  { get; init; }

    /// <summary>The parent indices of the skeleton. The root bone will have a parent index of -1.</summary>
    public int[] ParentIndices  { get; init; }

    /// <summary>The names of the bones in the skeleton. A bone's "ID" is represented by the index it has in this array.</summary>
    public string[] BoneNames  { get; init; }
}
