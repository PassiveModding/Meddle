using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab : ITab
{
    private readonly IClientState clientState;
    private readonly InteropService interopService;
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;

    public CharacterTab(
        IObjectTable objectTable,
        IClientState clientState,
        InteropService interopService,
        IPluginLog log)
    {
        this.interopService = interopService;
        this.log = log;
        this.objectTable = objectTable;
        this.clientState = clientState;
    }

    private ICharacter? SelectedCharacter { get; set; }

    public string Name => "Character";

    public int Order => 0;

    public void Draw()
    {
        if (!interopService.IsResolved) return;
        DrawObjectPicker();
    }


    public void Dispose() { }

    private void DrawObjectPicker()
    {
        ICharacter[] objects;
        if (clientState.LocalPlayer != null)
        {
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValid() && IsHuman(obj))
                                 .OrderBy(c => GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            // Lobby :)
            /*var chara = CharaSelectCharacterList.GetCurrentCharacter();
            if (chara != null)
            {
                objects = new[]
                {
                    (ICharacter)Activator.CreateInstance(typeof(ICharacter),
                                                         BindingFlags.NonPublic | BindingFlags.Instance, null,
                                                         new[] {(object?)(nint)chara}, null)!
                };
            }
            else
            {
                objects = Array.Empty<ICharacter>();
            }*/
            objects = Array.Empty<ICharacter>();
        }
        
        if (SelectedCharacter != null && !SelectedCharacter.IsValid())
        {
            SelectedCharacter = null;
        }

        if (SelectedCharacter == null)
        {
            SelectedCharacter = objects.FirstOrDefault() ?? clientState.LocalPlayer;
        }

        ImGui.Text("Select Character");
        var preview = SelectedCharacter != null ? GetCharacterDisplayText(SelectedCharacter) : "None";
        using (var combo = ImRaii.Combo("##Character", preview))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(GetCharacterDisplayText(character)))
                    {
                        SelectedCharacter = character;
                    }
                }
            }
        }

        if (SelectedCharacter != null)
        {
            DrawCharacterTree(SelectedCharacter);
        }
        else
        {
            ImGui.Text("No character selected");
        }
    }

    private Vector3 GetDistanceToLocalPlayer(IGameObject obj)
    {
        if (clientState.LocalPlayer is {Position: var charPos})
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }

    private string GetCharacterDisplayText(ICharacter obj)
    {
        return
            $"{obj.Address:X8}:{obj.GameObjectId:X} - {obj.ObjectKind} - {(string.IsNullOrWhiteSpace(obj.Name.TextValue) ? "Unnamed" : obj.Name.TextValue)} - {GetDistanceToLocalPlayer(obj).Length():0.00}y";
    }

    private unsafe void DrawCharacterTree(ICharacter character)
    {
        ImGui.Text($"Character: {character.Address:X8}");
        ImGui.Text($"Name: {character.Name.TextValue}");
        
        var charPtr = (CSCharacter*)character.Address;
        var human = (Human*)charPtr->GameObject.DrawObject;

        foreach (var modelPtr in human->ModelsSpan)
        {
            var model = modelPtr.Value;
            if (model == null)
            {
                ImGui.Text("Model is null");
                continue;
            }
            ImGui.Text($"{model->ModelResourceHandle->ResourceHandle.FileName}");
            ImGui.Indent();
            foreach (var materialPtr in model->MaterialsSpan)
            {
                var material = materialPtr.Value;
                if (material == null)
                {
                    ImGui.Text("Material is null");
                    continue;
                }
                ImGui.Text($"{material->MaterialResourceHandle->ResourceHandle.FileName}");
                
                ImGui.Indent();
                var shader = material->MaterialResourceHandle->ShpkNameString;
                ImGui.Text($"Shader: {shader}");
                foreach (var texPtr in material->TexturesSpan)
                {
                    var texHandle = texPtr.Texture;
                    if (texHandle == null)
                    {
                        ImGui.Text("Texture is null");
                        continue;
                    }
                    
                    ImGui.Text($"{texHandle->ResourceHandle.FileName}");
                }
                ImGui.Unindent();
            }
            ImGui.Unindent();
        }
    }
    
    public static bool IsHuman(ICharacter obj)
    {
        var drawObject = ((CSCharacter*)obj.Address)->GameObject.DrawObject;
        if (drawObject == null)
            return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return false;
        if (((CharacterBase*)drawObject)->GetModelType() != CharacterBase.ModelType.Human)
            return false;
        return true;
    }
}
