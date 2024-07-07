﻿using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.Utils;

public static class ObjectUtil
{
    public static unsafe bool IsValidCharacter(this ICharacter obj)
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
    
    public static Vector3 GetDistanceToLocalPlayer(this IClientState clientState, IGameObject obj)
    {
        if (clientState.LocalPlayer is {Position: var charPos})
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }

    public static string GetCharacterDisplayText(this IClientState clientState, ICharacter obj)
    {
        return
            $"{obj.Address:X8}:{obj.GameObjectId:X} - {obj.ObjectKind} - {(string.IsNullOrWhiteSpace(obj.Name.TextValue) ? "Unnamed" : obj.Name.TextValue)} - {clientState.GetDistanceToLocalPlayer(obj).Length():0.00}y";
    }
}