using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.Utils;

[Flags]
public enum CharacterValidationFlags
{
    None = 0,
    IsVisible = 1 << 0
}

public static class ObjectTableExtensions
{
    extension(IObjectTable table)
    {
        public ICharacter[] GetCharacters(CharacterValidationFlags flags = CharacterValidationFlags.None)
        {
            if (table.LocalPlayer != null)
            {
                return table.OfType<ICharacter>()
                            .Where(obj => obj.IsValid() && obj.IsValidCharacterBase(flags))
                            .OrderBy(c => table.GetDistanceToLocalPlayer(c).LengthSquared())
                            .ToArray();
            }
            else
            {
                // login/char creator produces "invalid" characters but are still usable I guess
                return table.OfType<ICharacter>()
                            .Where(obj => obj.IsValidHuman(flags))
                            .OrderBy(c => table.GetDistanceToLocalPlayer(c).LengthSquared())
                            .ToArray();
            }
        }
        
        public Vector3 GetDistanceToLocalPlayer(IGameObject obj)
        {
            if (table.LocalPlayer == null)
            {
                return Vector3.Zero;
            }
            
            var lpPos = table.LocalPlayer.Position;
            var objPos = obj.Position;
            return new Vector3(
                objPos.X - lpPos.X,
                objPos.Y - lpPos.Y,
                objPos.Z - lpPos.Z);
        }
    }
    
    extension(ICharacter character)
    {
        public unsafe bool IsValidHuman(CharacterValidationFlags flags = CharacterValidationFlags.None)
        {
            var drawObject = ((CSCharacter*)character.Address)->GameObject.DrawObject;
            if (drawObject == null)
                return false;
            if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
                return false;
            if (((CharacterBase*)drawObject)->GetModelType() != CharacterBase.ModelType.Human)
                return false;

            if (flags.HasFlag(CharacterValidationFlags.IsVisible) && !drawObject->IsVisible)
            {
                return false;
            }

            return true;
        }

        public unsafe bool IsValidCharacterBase(CharacterValidationFlags flags = CharacterValidationFlags.None)
        {
            if (!character.IsValid())
                return false;
            var drawObject = ((CSCharacter*)character.Address)->GameObject.DrawObject;
            if (drawObject == null)
                return false;
            if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
                return false;

            if (flags.HasFlag(CharacterValidationFlags.IsVisible) && !drawObject->IsVisible)
            {
                return false;
            }

            return true;
        }
    }
}
