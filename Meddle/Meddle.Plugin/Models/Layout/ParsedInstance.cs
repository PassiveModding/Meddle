using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Lumina.Excel.GeneratedSheets;

namespace Meddle.Plugin.Models.Layout;

public abstract class ParsedInstance
{
    public nint Id;
    public abstract InstanceType Type { get; }
    public Transform Transform;
    public List<ParsedInstance> Children = new();
    
    public ParsedInstance[] Flatten()
    {
        var list = new List<ParsedInstance> { this };
        foreach (var child in Children)
        {
            list.AddRange(child.Flatten());
        }

        return list.ToArray();
    }
}

public class ParsedUnsupportedInstance : ParsedInstance
{
    public ParsedUnsupportedInstance(InstanceType type)
    {
        Type = type;
    }
    
    public override InstanceType Type { get; }
}

public class ParsedSharedInstance : ParsedInstance
{
    public override InstanceType Type => InstanceType.SharedGroup;
}

public class ParsedHousingInstance : ParsedSharedInstance
{
    public override InstanceType Type => InstanceType.SharedGroup;
    
    public Stain? Stain;
    public Item? Item;
    public string Name;
    public ObjectKind Kind;
}
        
public class ParsedBgPartsInstance : ParsedInstance
{
    public override InstanceType Type => InstanceType.BgPart;
    
    public string Path;
}

public class ParsedLightInstance : ParsedInstance
{
    public override InstanceType Type => InstanceType.Light;
}

public class ParsedLayer
{
    public nint Id;
    public List<ParsedInstance> Instances = [];
}
