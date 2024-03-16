using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models;

public unsafe class AttachedChild
{
    public Skeleton Skeleton { get; }
    public IReadOnlyList<Model> Models { get;}
    public Attach Attach { get; }
        
    public AttachedChild(Pointer<CharacterBase> character) : this(character.Value)
    {
    }
    
    public AttachedChild(CharacterBase* character)
    {
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
