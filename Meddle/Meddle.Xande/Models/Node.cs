using Penumbra.Api.Enums;

namespace Meddle.Xande.Models;

public class Node {
    public string Name { get; set; }
    public ResourceType Type { get; set; }
    public Node(string fullPath, string gamePath, string name, ResourceType type, Node[]? children = null)
    {
        FullPath = fullPath;
        GamePath = gamePath;
        Name = name;
        Type = type;
        Children = children ?? Array.Empty<Node>();
    }

    // custom setter to replace \\ in non-rooted paths with /
    private string _fullPath = null!;
    public string FullPath { 
        set => _fullPath = Path.IsPathRooted( value ) ? value : value.Replace( "\\", "/" );
        get => _fullPath; 
    }
    public string GamePath { get; set; }
    public Node[] Children { get; set; }
}