using SharpGLTF.Scenes;
using SharpGLTF.Transforms;


namespace Meddle.Utils;

public class BoneNodeBuilder(string name) : NodeBuilder(name)
{
    public string BoneName { get; } = name;
    public string PartialSkeletonHandle { get; set; }
    public int BoneIndex { get; set; }
    public int PartialSkeletonIndex { get; set; }
    public AffineTransform? MeddleLocalTransform { get; set; }
    public AffineTransform? MeddleModelTransform { get; set; }
    
    /// <summary>
    /// Sets the suffix of this bone and all its children.
    /// If the suffix is null, the bone name will be reset to the original bone name.
    /// If the suffix is not null, the bone name will be set to the original bone name with the suffix appended.
    /// </summary>
    public void SetSuffixRecursively(int? suffix)
    {
        Name = suffix != null ? $"{BoneName}_{suffix}" : BoneName;

        foreach (var child in VisualChildren)
        {
            if (child is BoneNodeBuilder boneChild)
                boneChild.SetSuffixRecursively(suffix);
        }
    }
    
    public void SetSuffixRecursively(string? suffix)
    {
        Name = suffix != null ? $"{BoneName}_{suffix}" : BoneName;

        foreach (var child in VisualChildren)
        {
            if (child is BoneNodeBuilder boneChild)
                boneChild.SetSuffixRecursively(suffix);
        }
    }
}
