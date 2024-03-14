using System.Runtime.InteropServices;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Shader;
using Meddle.Plugin.Enums;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.Models;

public unsafe class CharacterTree
{
    public string Name { get; set; }
    public Transform Transform { get; set; }
    public Skeleton Skeleton { get; set; }
    public List<Model> Models { get; set; }
    public Attach Attach { get; set; }

    public ushort? RaceCode { get; set; }

    public CustomizeParameter? CustomizeParameter { get; set; }
    
    public List<CharacterTree>? AttachedChildren { get; set; }

    public CharacterTree(CSCharacter* character) : this((CharacterBase*)character->GameObject.DrawObject)
    {
        Name = MemoryHelper.ReadStringNullTerminated((nint)character->GameObject.GetName());

        AttachedChildren = new();
        foreach (var weaponData in character->DrawData.WeaponDataSpan)
        {
            if (weaponData.Model == null)
                continue;
            var attach = &weaponData.Model->CharacterBase.Attach;
            if (attach->ExecuteType == 0)
                continue;

            AttachedChildren.Add(new(&weaponData.Model->CharacterBase));
        }
    }

    public CharacterTree(CharacterBase* character)
    {
        var name = stackalloc byte[256];
        name = character->ResolveRootPath(name, 256);
        Name = name != null ? MemoryHelper.ReadString((nint)name, 256) : string.Empty;

        var modelType = character->GetModelType();
        var human = modelType == CharacterBase.ModelType.Human ? (Human*)character : null;
        if (human != null && human->CustomizeParameterCBuffer != null)
        {
            var cp = human->CustomizeParameterCBuffer->LoadBuffer<CustomizeParameter>(0, 1);
            if (cp != null && cp.Length > 0)
            {
                CustomizeParameter = cp[0];
            }
        
            RaceCode = human->RaceSexId;
        }
        
        Transform = new(character->DrawObject.Object.Transform);
        Skeleton = new(character->Skeleton);
        Models = new();
        for (var i = 0; i < character->SlotCount; ++i)
        {
            if (character->Models[i] == null)
                continue;
            
            Models.Add(new(character->Models[i], character->ColorTableTextures + (i * 4)));
        }
        Attach = new(&character->Attach);
    }
}
