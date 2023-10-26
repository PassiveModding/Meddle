using Xande.Enums;

namespace Meddle.Xande.Models;

public class CharacterResourceMap
{
    public readonly GenderRace RaceCode;
    public readonly Dictionary<string, Dictionary<string, List<string>>> ModelData;

    public CharacterResourceMap(GenderRace raceCode, Dictionary<string, Dictionary<string, List<string>>> modelData)
    {
        RaceCode = raceCode;
        ModelData = modelData;
    }
}