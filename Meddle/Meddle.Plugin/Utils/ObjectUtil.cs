using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using SharpGLTF.Transforms;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.Utils;

public static class ObjectUtil
{
    public static AffineTransform ToAffine(this FFXIVClientStructs.FFXIV.Client.LayoutEngine.Transform transform)
    {
        return new AffineTransform(transform.Scale, transform.Rotation, transform.Translation);
    }
    
    public static string ToFormatted(this Vector3 vector)
    {
        return $"<X: {vector.X:0.00} Y: {vector.Y:0.00} Z: {vector.Z:0.00}>";
    }

    public static string ToFormatted(this Quaternion vector)
    {
        return $"<X: {vector.X:0.00} Y: {vector.Y:0.00} Z: {vector.Z:0.00} W: {vector.W:0.00}>";
    }

    public static unsafe bool IsValidHuman(this ICharacter obj)
    {
        var drawObject = ((CSCharacter*)obj.Address)->GameObject.DrawObject;
        if (drawObject == null)
            return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return false;
        if (((CharacterBase*)drawObject)->GetModelType() != CharacterBase.ModelType.Human)
            return false;

        if (!drawObject->IsVisible)
        {
            return false;
        }

        return true;
    }

    public static unsafe bool IsValidCharacterBase(this ICharacter obj)
    {
        var drawObject = ((CSCharacter*)obj.Address)->GameObject.DrawObject;
        if (drawObject == null)
            return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return false;

        if (!drawObject->IsVisible)
        {
            return false;
        }

        return true;
    }

    public static Vector3 GetDistanceToLocalPlayer(this IClientState clientState, IGameObject obj)
    {
        if (clientState.LocalPlayer is {Position: var charPos})
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }
}
