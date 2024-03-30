using System.Runtime.InteropServices;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Shader;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Enums;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.Models;

public unsafe class CharacterTree
{
    public string Name { get; }
    public Transform Transform { get;}
    public Skeleton Skeleton { get; }
    public IReadOnlyList<Model> Models { get; }
    public GenderRace RaceCode { get; }
    public CustomizeParameters? CustomizeParameter { get; set; }
    
    public IReadOnlyList<AttachedChild> AttachedChildren { get; }
    //public Ornament? Ornament { get; }

    public CharacterTree(Pointer<CSCharacter> character) : this(character.Value)
    {
    }
    
    public CharacterTree(CSCharacter* character) : 
        this((CharacterBase*)character->GameObject.DrawObject)
    {
        Name = MemoryHelper.ReadStringNullTerminated((nint)character->GameObject.GetName());

        var attachedChildren = new List<AttachedChild>();
        if (character->DrawData.IsWeaponHidden == false)
        {
            foreach (var weaponData in character->DrawData.WeaponDataSpan)
            {
                if (weaponData.Model == null)
                    continue;
                var attach = &weaponData.Model->CharacterBase.Attach;
                if (attach->ExecuteType == 0)
                    continue;

                attachedChildren.Add(new AttachedChild(&weaponData.Model->CharacterBase));
            }
        }
        
        if (character->Ornament.OrnamentObject != null && character->Ornament.OrnamentId != 0)
        {
            var ornamentObject = character->Ornament.OrnamentObject;
            if (ornamentObject != null)
            {
                // ExecuteType 3 (I think)
                var ornamentCharacter = (CharacterBase*)ornamentObject->Character.GameObject.DrawObject;
                attachedChildren.Add(new AttachedChild(ornamentCharacter));
            }
        }

        if (character->Mount.MountObject != null && character->Mount.MountId != 0)
        {
            var mountObject = character->Mount.MountObject;
            if (mountObject != null)
            {
                // ExecuteType 0?
                var mountCharacter = (CharacterBase*)mountObject->GameObject.DrawObject;
                attachedChildren.Add(new AttachedChild(mountCharacter));
            }
        }

        AttachedChildren = attachedChildren;
    }
    
    public CharacterTree(Pointer<CharacterBase> character) : this(character.Value)
    {
    }

    public CharacterTree(CharacterBase* character)
    {
        Name = character->ResolveRootPath();

        var modelType = character->GetModelType();
        var human = modelType == CharacterBase.ModelType.Human ? (Human*)character : null;
        if (human != null)
        {
            RaceCode = (GenderRace)human->RaceSexId;
            
            if (human->CustomizeParameterCBuffer != null)
            {
                var cp = human->CustomizeParameterCBuffer->LoadBuffer<CustomizeParameter>(0, 1);
                if (cp != null && cp.Length > 0)
                {
                    var isHrothgar = RaceCode is GenderRace.HrothgarFemale 
                                         or GenderRace.HrothgarMale 
                                         or GenderRace.HrothgarFemaleNpc 
                                         or GenderRace.HrothgarMaleNpc;
                    CustomizeParameter = new CustomizeParameters(cp[0], isHrothgar);
                }
            }
        }
        
        Transform = new Transform(character->DrawObject.Object.Transform);
        Skeleton = new Skeleton(character->Skeleton);
        var models = new List<Model>();
        for (var i = 0; i < character->SlotCount; ++i)
        {
            if (character->Models[i] == null)
                continue;
            
            models.Add(new Model(character->Models[i], character->ColorTableTextures + (i * 4), character));
        }

        Models = models;
        AttachedChildren ??= Array.Empty<AttachedChild>();
    }
}
