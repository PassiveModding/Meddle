using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

// Penumbra ModelExport
namespace Meddle.Plugin.Models;

/// <summary> Representation of a glTF-compatible skeleton. </summary>
public struct GltfSkeleton
{
    /// <summary> Root node of the skeleton. </summary>
    public NodeBuilder Root;

    /// <summary> Flattened list of skeleton nodes. </summary>
    public List<NodeBuilder> Joints;

    /// <summary> Mapping of bone names to their index within the joints array. </summary>
    public Dictionary<string, int> Names;

    public (NodeBuilder, int) GenerateBone(string name)
    {
        var node  = new NodeBuilder(name);
        var index = Joints.Count;
        Names[name] = index;
        Joints.Add(node);
        Root.AddNode(node);
        return (node, index);
    }
    
    /// <summary> Convert XIV skeleton data into a glTF-compatible node tree, with mappings. </summary>
    public GltfSkeleton(IEnumerable<XivSkeleton> skeletons)
    {
        NodeBuilder? root = null;
        var names = new Dictionary<string, int>();
        var joints = new List<NodeBuilder>();

        // Flatten out the bones across all the received skeletons, but retain a reference to the parent skeleton for lookups.
        var iterator = skeletons.SelectMany(skeleton => skeleton.Bones.Select(bone => (skeleton, bone)));
        foreach (var (skeleton, bone) in iterator)
        {
            if (names.ContainsKey(bone.Name))
                continue;

            var node = new NodeBuilder(bone.Name);
            names[bone.Name] = joints.Count;
            joints.Add(node);

            node.SetLocalTransform(new AffineTransform(
                bone.Transform.Scale,
                bone.Transform.Rotation,
                bone.Transform.Translation
            ), false);

            if (bone.ParentIndex == -1)
            {
                root = node;
                continue;
            }

            var parent = joints[names[skeleton.Bones[bone.ParentIndex].Name]];
            parent.AddNode(node);
        }


        Root = root ?? throw new Exception("Root node not found.");
        Joints = joints;
        Names = names;
    }
    
    
}
