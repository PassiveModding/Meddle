using Dalamud.Plugin.Services;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Meddle.Xande.Utility;

public static class CharacterUtility
{
    public static unsafe bool HasDrawObject(ushort gameObjectId, IObjectTable objectTable)
    {
        var characters = objectTable.OfType<Character>();

        var match = characters.FirstOrDefault(x => x.ObjectIndex == gameObjectId);
        if (match == null || !match.IsValid())
        {
            return false;
        }

        var gameObject = (GameObject*)match.Address;
        var drawObject = gameObject->GetDrawObject();
        if (drawObject == null)
        {
            return false;
        }

        return true;
    }
}