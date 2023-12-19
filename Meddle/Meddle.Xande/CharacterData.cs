using Penumbra.Api;
using Penumbra.Api.Enums;
using SharpGLTF.Transforms;

namespace Meddle.Xande;

[Obsolete("Use NewTree", true)]
public class CharacterData
{
    public required CharacterTree Resources { get; set; }
    public Dictionary<string, AffineTransform>? Pose { get; set; }
    //public List<ModelMeta>? ModelMetas { get; set; }
    //public List<HkSkeleton.WeaponData>? WeaponDatas { get; set; }
}

[Obsolete("Use NewTree", true)]
public class CharacterTree
{
    public string Name { get; init; }
    public ushort RaceCode { get; init; }
    public List<CharacterNode> Nodes { get; init; }

    public CharacterTree(Ipc.ResourceTree tree)
    {
        Name = tree.Name;
        RaceCode = tree.RaceCode;
        Nodes = tree.Nodes.Select(x => new CharacterNode(x)).ToList();
    }
}

[Obsolete("Use NewTree", true)]
public class CharacterNode
{
    public ResourceType Type { get; init; }
    public string? Name { get; init; }
    public string? GamePath { get; init; }
    public string ActualPath { get; init; }
    public nint ObjectAddress { get; init; }
    public nint ResourceHandle { get; init; }
    public List<CharacterNode> Children { get; init; }

    public string FullPath =>
        Path.IsPathRooted(ActualPath) ?
            ActualPath :
            ActualPath.Replace('\\', '/');

    public CharacterNode(Ipc.ResourceNode node)
    {
        Type = node.Type;
        Name = node.Name;
        GamePath = node.GamePath;
        ActualPath = node.ActualPath;
        ObjectAddress = node.ObjectAddress;
        ResourceHandle = node.ResourceHandle;
        Children = node.Children.Select(x => new CharacterNode(x)).ToList();
    }
}
