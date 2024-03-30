using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Models.ExportRequest;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utility;
using GameObject = Dalamud.Game.ClientState.Objects.Types.GameObject;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.UI;

public class ObjectTab : ITab
{
    public ObjectTab(
        DalamudPluginInterface pluginInterface,
        Configuration configuration,
        IObjectTable objectTable,
        IClientState clientState,
        ExportManager exportManager,
        IPluginLog log)
    {
        Log = log;
        PluginInterface = pluginInterface;
        Configuration = configuration;
        ObjectTable = objectTable;
        ClientState = clientState;
        ExportManager = exportManager;
    }

    public IPluginLog Log { get; }
    public DalamudPluginInterface PluginInterface { get; }
    public Configuration Configuration { get; }
    public IObjectTable ObjectTable { get; }
    public IClientState ClientState { get; }
    public ExportManager ExportManager { get; }
    public string Name => "Objects";
    public int Order => 2;
    public int SelectedObjectIndex { get; set; }
    public CharacterTree? Tree { get; set; }

    public unsafe void Draw()
    {
        if (Plugin.CsResolved == false) return;
        if (ClientState.LocalPlayer == null) return;
        var objects = ObjectTable.Where(obj => obj.IsValid())
                                 .OrderBy(obj => GetDistance(obj).LengthSquared())
                                 .ToArray();


        // Dropdown
        var initialIndex = SelectedObjectIndex;
        var selectedObject = objects.FirstOrDefault(x => x.ObjectIndex == SelectedObjectIndex);
        if (ImGui.BeginCombo("##object", selectedObject == null ? "Select an object" : FormatName(selectedObject)))
        {
            foreach (var obj in objects)
            {
                if (ImGui.Selectable(FormatName(obj), selectedObject == obj))
                {
                    selectedObject = obj;
                    SelectedObjectIndex = obj.ObjectIndex;
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.ArrowButton("##prev", ImGuiDir.Left))
        {
            if (selectedObject != null)
            {
                var idx = Array.FindIndex(objects, x => x.ObjectIndex == SelectedObjectIndex);
                if (idx > 0)
                {
                    selectedObject = objects[idx - 1];
                    SelectedObjectIndex = selectedObject.ObjectIndex;
                }
            }
            else
            {
                selectedObject = objects.FirstOrDefault();
                SelectedObjectIndex = selectedObject?.ObjectIndex ?? 0;
            }
        }

        ImGui.SameLine();

        if (ImGui.ArrowButton("##next", ImGuiDir.Right))
        {
            if (selectedObject != null)
            {
                var idx = Array.FindIndex(objects, x => x.ObjectIndex == SelectedObjectIndex);
                if (idx < objects.Length - 1)
                {
                    selectedObject = objects[idx + 1];
                    SelectedObjectIndex = selectedObject.ObjectIndex;
                }
            }
            else
            {
                selectedObject = objects.FirstOrDefault();
                SelectedObjectIndex = selectedObject?.ObjectIndex ?? 0;
            }
        }

        if (selectedObject == null) return;
        if (initialIndex != SelectedObjectIndex)
        {
            Log.Info("Selected object: " + selectedObject.Name);
            Tree = null;
        }
        
        DrawGameObjectInfo(selectedObject);
    }

    private unsafe void DrawCharacterInfo(CSCharacter* character)
    {
        Tree ??= new CharacterTree(character);
        
        if (ImGui.Button("Refresh"))
        {
            Tree = new CharacterTree(character);
        }

        ImGui.Text("Character Info");
        ImGui.Text($"Name: {Tree.Name}");
        
        IExportRequest? exportRequest = null;
        if (CharacterTab.DrawExportButton("Export", ExportManager.IsExporting))
        {
            exportRequest = new ExportTreeRequest(Tree);
        }
        
        if (Tree.Models.Count > 0 && ImGui.CollapsingHeader("Models"))
        {
            using var table = ImRaii.Table("##models", 1, ImGuiTableFlags.Borders);
            foreach (var model in Tree.Models)
            {
                ImGui.TableNextColumn();
                CharacterTab.DrawModel(model, ExportManager.IsExporting, ExportManager.CancellationTokenSource,
                                       out var me, out var em);

                if (me != null)
                {
                    exportRequest = new MaterialExportRequest(me, Tree.CustomizeParameter);
                }
                
                if (em != null)
                {
                    exportRequest = new ExportModelRequest(em, Tree.Skeleton);
                }
            }
        }

        if (Tree.AttachedChildren.Count > 0 && ImGui.CollapsingHeader("Attaches"))
        {
            foreach (var child in Tree.AttachedChildren)
            {
                var skeleton = Tree.Skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx];
                var bone = skeleton.HkSkeleton!.BoneNames[child.Attach.BoneIdx];
                if (ImGui.CollapsingHeader($"Attach at {bone ?? "unknown"}##{child.GetHashCode()}"))
                {
                    if (child.Attach.OffsetTransform is { } ct)
                    {
                        ImGui.Text($"Position: {ct.Translation}");
                        ImGui.Text($"Rotation: {ct.Rotation}");
                        ImGui.Text($"Scale: {ct.Scale}");
                    }
                    
                    ImGui.Text($"Execute Type: {child.Attach.ExecuteType}");
                    using var table = ImRaii.Table("##attachmodels", 1, ImGuiTableFlags.Borders);
                    foreach (var model in child.Models)
                    {
                        ImGui.TableNextColumn();
                        CharacterTab.DrawModel(model, ExportManager.IsExporting, ExportManager.CancellationTokenSource,
                                               out var me, out var em);

                        if (me != null)
                        {
                            exportRequest = new MaterialExportRequest(me);
                        }
                        
                        if (em != null)
                        {
                            exportRequest = new ExportModelRequest(em, child.Skeleton);
                        }
                    }
                }
            }
        }
        
        if (exportRequest != null)
        {
            CharacterTab.HandleExportRequest(exportRequest, new ExportLogger(Log), ExportManager, Configuration);
        }
    }

    private unsafe void DrawGameObjectInfo(GameObject selectedObject)
    {
        ImGui.Text($"Name: {FormatName(selectedObject)}");
        ImGui.Text($"Kind: {selectedObject.ObjectKind}");
        ImGui.Text($"ID: {selectedObject.ObjectId}");
        ImGui.Text($"Index: {selectedObject.ObjectIndex}");
        ImGui.Text($"Distance: {GetDistance(selectedObject).Length():0.00}y");
        ImGui.Text($"Address: {selectedObject.Address:X}");
        
        if (selectedObject is Character character)
        {
            var drawObject = ((CSCharacter*)character.Address)->GameObject.DrawObject;
            if (drawObject == null) 
                return;
            if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
                return;
            
            var csCharacter = (CSCharacter*)character.Address;
            DrawCharacterInfo(csCharacter);
        }
    }

    private string FormatName(GameObject obj)
    {
        var distance = GetDistance(obj);
        return $"[{obj.ObjectKind}]{obj.Name} {distance.Length():0.00}y";
    }

    private Vector3 GetDistance(GameObject obj)
    {
        if (ClientState.LocalPlayer is {Position: var charPos})
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }

    public void Dispose() { }
}
