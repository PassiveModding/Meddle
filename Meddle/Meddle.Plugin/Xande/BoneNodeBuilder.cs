using SharpGLTF.Scenes;

namespace Meddle.Plugin.Xande;

public class BoneNodeBuilder(string name) : NodeBuilder(name)
{
    public string BoneName { get; } = name;
    public int? Suffix { get; private set; }

    /// <summary>
    /// Sets the suffix of this bone and all its children.
    /// If the suffix is null, the bone name will be reset to the original bone name.
    /// If the suffix is not null, the bone name will be set to the original bone name with the suffix appended.
    /// </summary>
    public void SetSuffixRecursively(int? suffix)
    {
        Suffix = suffix;
        if (suffix is { } val)
        {
            Name = $"{BoneName}_{val}";
        }
        else
        {
            Name = BoneName;
        }

        foreach (var child in VisualChildren)
        {
            if (child is BoneNodeBuilder boneChild)
                boneChild.SetSuffixRecursively(suffix);
        }
    }
}
