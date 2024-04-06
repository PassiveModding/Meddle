using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Services;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration configuration;
    private readonly InteropService interopService;
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;

    public CharacterTab(
        Configuration configuration,
        IObjectTable objectTable,
        IClientState clientState,
        ExportManager exportManager,
        InteropService interopService,
        IPluginLog log)
    {
        this.interopService = interopService;
        this.log = log;
        this.configuration = configuration;
        this.objectTable = objectTable;
        this.clientState = clientState;
        ExportManager = exportManager;
    }

    private ExportManager ExportManager { get; }
    private Character? SelectedCharacter { get; set; }

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
        Character[] objects;
        if (clientState.LocalPlayer != null)
        {
            objects = objectTable.OfType<Character>()
                                 .Where(obj => obj.IsValid() && IsHuman(obj))
                                 .OrderBy(c => GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            // Lobby :)
            var chara = CharaSelectCharacterList.GetCurrentCharacter();
            if (chara != null)
            {
                objects = new[]
                {
                    (Character)Activator.CreateInstance(typeof(Character),
                                                        BindingFlags.NonPublic | BindingFlags.Instance, null,
                                                        new[] {(object?)(nint)chara}, null)!
                };
            }
            else
            {
                objects = Array.Empty<Character>();
            }
        }

        /*if (ClientState.IsGPosing)
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
        }*/

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

    private Vector3 GetDistanceToLocalPlayer(GameObject obj)
    {
        if (clientState.LocalPlayer is {Position: var charPos})
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }

    private string GetCharacterDisplayText(Character obj)
    {
        return
            $"{obj.Address:X8}:{obj.ObjectId:X} - {obj.ObjectKind} - {(string.IsNullOrWhiteSpace(obj.Name.TextValue) ? "Unnamed" : obj.Name.TextValue)} - {GetDistanceToLocalPlayer(obj).Length():0.00}y";
    }
}
