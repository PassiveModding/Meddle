using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utility;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab : ITab
{
    public CharacterTab(DalamudPluginInterface pluginInterface,
                        IObjectTable objectTable,
                        IClientState clientState,
                        ModelManager modelConverter,
                        IPluginLog log)
    {
        Log = log;
        PluginInterface = pluginInterface;
        ObjectTable = objectTable;
        ClientState = clientState;
        ModelConverter = modelConverter;
    }
    public string Name => "Character";

    public int Order => 0;

    private IPluginLog Log { get; }
    private DalamudPluginInterface PluginInterface { get; }
    private IObjectTable ObjectTable { get; }
    private IClientState ClientState { get; }
    private ModelManager ModelConverter { get; }
    private Character? SelectedCharacter { get; set; }

    public void Draw()
    {
        DrawObjectPicker();
    }

    private void DrawObjectPicker()
    {
        Character[] objects;
        if (ClientState.LocalPlayer != null)
            objects = ObjectTable.OfType<Character>()
                                 .Where(obj => obj.IsValid() && IsHuman(obj))
                                 .OrderBy(c => GetCharacterDistance(c).LengthSquared())
                                 .ToArray();
        else
        {
            try
            {
                // Lobby :)
                // TODO: Maybe don't call this until post tick is done
                var chara = CharaSelectCharacterList.GetCurrentCharacter();
                if (chara != null)
                    objects = new[] { (Character)Activator.CreateInstance(typeof(Character), BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { (object?)(nint)chara }, null)! };
                else
                    objects = Array.Empty<Character>();
            }
            catch
            {
                return;
            }

        }

        if (ClientState.IsGPosing)
        {
            // Within gpose, only show characters that are gpose actors
            objects = objects.Where(x => x.ObjectIndex is >= 201 and < 239).ToArray();
            if (SelectedCharacter?.ObjectIndex is < 201 or >= 239)
                SelectedCharacter = null;
        }
        else
        {
            if (SelectedCharacter?.ObjectIndex is >= 201 and < 239)
                SelectedCharacter = null;
        }

        if (SelectedCharacter != null && !SelectedCharacter.IsValid())
        {
            SelectedCharacter = null;
        }

        if (SelectedCharacter == null)
        {
            SelectedCharacter = objects.FirstOrDefault() ?? ClientState.LocalPlayer;
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
            if (ImGui.CollapsingHeader("Character Pose"))
            {
                DrawPoseInfo(SelectedCharacter, ClientState);
            }
            
            if (ImGui.CollapsingHeader($"Character Tree", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawCharacterTree(SelectedCharacter);
            }
        }
        else
        {
            ImGui.Text("No character selected");
        }
    }

    private Vector3 GetCharacterDistance(Character obj) {
        if (ClientState.LocalPlayer is { Position: var charPos })
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }

    private string GetCharacterDisplayText(Character obj) =>
        $"{obj.Address:X8}:{obj.ObjectId:X} - {obj.ObjectKind} - {(string.IsNullOrWhiteSpace(obj.Name.TextValue) ? "Unnamed" : obj.Name.TextValue)} - {GetCharacterDistance(obj).Length():0.00}y";

    private static bool IsHuman(Character obj)
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

    public void Dispose()
    {
        ExportCts?.Cancel();
        ExportCts?.Dispose();
    }
}
