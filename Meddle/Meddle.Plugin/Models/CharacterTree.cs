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
    public IReadOnlyList<Model> Models { get; set; }
    public Attach Attach { get; set; }

    public ushort? RaceCode { get; set; }

    public CustomizeParameters? CustomizeParameter { get; set; }
    
    public IReadOnlyList<CharacterTree>? AttachedChildren { get; set; }

    public CharacterTree(CSCharacter* character) : this((CharacterBase*)character->GameObject.DrawObject)
    {
        Name = MemoryHelper.ReadStringNullTerminated((nint)character->GameObject.GetName());

        var attachedChildren = new List<CharacterTree>();
        foreach (var weaponData in character->DrawData.WeaponDataSpan)
        {
            if (weaponData.Model == null)
                continue;
            var attach = &weaponData.Model->CharacterBase.Attach;
            if (attach->ExecuteType == 0)
                continue;

            attachedChildren.Add(new CharacterTree(&weaponData.Model->CharacterBase));
        }
        
        AttachedChildren = attachedChildren;
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
                CustomizeParameter = new CustomizeParameters(cp[0]);
            }
        
            RaceCode = human->RaceSexId;
        }
        
        Transform = new Transform(character->DrawObject.Object.Transform);
        Skeleton = new Skeleton(character->Skeleton);
        var models = new List<Model>();
        for (var i = 0; i < character->SlotCount; ++i)
        {
            if (character->Models[i] == null)
                continue;
            
            models.Add(new Model(character->Models[i], character->ColorTableTextures + (i * 4)));
        }
        
        Models = models;
        Attach = new Attach(&character->Attach);
    }
}
