using Xande.Enums;

namespace Meddle.Xande.Models;

public class ResourceTree {
    public ResourceTree(string name, GenderRace raceCode = GenderRace.Unknown, Node[]? nodes = null)
    {
        Name = name;
        RaceCode = raceCode;
        Nodes = nodes ?? Array.Empty<Node>();
    }

    public string Name { get; set; }
    public Node[] Nodes { get; set; }
    public GenderRace RaceCode { get; set; }
}