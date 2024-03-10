using Dalamud.Plugin;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Models.Customize;
using Penumbra.Api;
using Penumbra.Api.Enums;
using Penumbra.GameData.Enums;

namespace Meddle.Plugin.Models.ResourceTree;

public class ResourceTree
{
    public Customize.Customize Customize { get; }
    public string Name { get; }
    public ResourceNode[] Nodes { get; }
    public GenderRace GenderRace { get; }

    public static ResourceTree GetResourceTree(DalamudPluginInterface pi, ushort gameObjectId, Customize.Customize customizeResult)
    {
        var resourceTree = Ipc.GetGameObjectResourceTrees.Subscriber(pi).Invoke(true, gameObjectId)[0]!;
        return new ResourceTree(resourceTree, customizeResult);
    }
    
    public ResourceTree(Ipc.ResourceTree resourceTree, Customize.Customize customize)
    {
        Customize = customize;
        Name = resourceTree.Name;
        Nodes = resourceTree.Nodes.Select(n => new ResourceNode(n)).ToArray();
        GenderRace = (GenderRace)resourceTree.RaceCode;
    }
    

    public class ResourceNode
    {
        public ResourceNode[] Children { get; }
        public string Name { get; }
        public string GamePath { get; }
        public string ActualPath { get; }
        public ResourceType Type { get; }
        
        public ResourceNode(Ipc.ResourceNode node)
        {
            Children = node.Children.Select(n => new ResourceNode(n)).ToArray();
            Name = node.Name;
            GamePath = node.GamePath;
            ActualPath = node.ActualPath;
            Type = node.Type;
        }
    }
}