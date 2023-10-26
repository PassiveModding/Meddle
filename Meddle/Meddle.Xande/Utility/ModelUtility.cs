using System.Text.RegularExpressions;
using Lumina.Data.Files;
using Meddle.Lumina.Models;
using Meddle.Xande.Models;
using SharpGLTF.Scenes;
using Xande;
using Xande.Havok;

namespace Meddle.Xande.Utility;

public static class ModelUtility
{
    /// <summary>Builds a skeleton tree from a list of .sklb paths.</summary>
    /// <param name="skeletons">A list of HavokXml instances.</param>
    /// <param name="root">The root bone node.</param>
    /// <returns>A mapping of bone name to node in the scene.</returns>
    public static Dictionary<string, NodeBuilder> GetBoneMap(HavokXml[] skeletons, out NodeBuilder? root)
    {
        Dictionary<string, NodeBuilder> boneMap = new();
        root = null;

        foreach (var xml in skeletons)
        {
            var skeleton = xml.GetMainSkeleton();
            var boneNames = skeleton.BoneNames;
            var refPose = skeleton.ReferencePose;
            var parentIndices = skeleton.ParentIndices;

            for (var j = 0; j < boneNames.Length; j++)
            {
                var name = boneNames[j];
                if (boneMap.ContainsKey(name)) continue;

                var bone = new NodeBuilder(name);
                bone.SetLocalTransform(XmlUtils.CreateAffineTransform(refPose[j]), false);

                var boneRootId = parentIndices[j];
                if (boneRootId != -1)
                {
                    var parent = boneMap[boneNames[boneRootId]];
                    parent.AddNode(bone);
                }
                else
                {
                    root = bone;
                }

                boneMap[name] = bone;
            }
        }

        return boneMap;
    }
    
    private static readonly object ModelLoadLock = new();
    public static Model GetModel(LuminaManager manager, string path)
    {
        lock (ModelLoadLock)
        {
            var mdlFile = manager.GetFile<MdlFile>(path);
            return mdlFile != null
                ? new Model(mdlFile)
                : throw new FileNotFoundException();
        }
    }
    
    public static bool TryGetModel( this LuminaManager manager, Node node, ushort? deform, out string path, out Model? model ) {

            path = node.FullPath;
            if( TryLoadModel( node.FullPath, out model ) ) { return true; }

            if( TryLoadModel( node.GamePath, out model ) ) { return true; }

            if( TryLoadRacialModel( node.GamePath, deform, out var newPath, out model ) ) { return true; }
            
            return false;

            bool TryLoadRacialModel( string path, ushort? cDeform, out string nPath, out Model? model ) {
                nPath = path;
                model = null;
                if( cDeform == null ) { return false; }

                nPath = Regex.Replace( path, @"c\d+", $"c{cDeform}" );
                try
                {
                    model = GetModel(manager, nPath);
                    return true;
                }
                catch { return false; }
            }

            bool TryLoadModel(string path, out Model? model)
            {
                model = null;
                try
                {
                    model = GetModel(manager, path);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

    }
}