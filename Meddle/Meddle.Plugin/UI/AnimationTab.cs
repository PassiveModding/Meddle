using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.UI;

public class AnimationTab : ITab
{
    private readonly IFramework framework;
    private readonly ILogger<AnimationTab> logger;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly ExportUtil exportUtil;
    private readonly Configuration config;
    public string Name => "Animation";
    public int Order => 2;
    public bool DisplayTab => true;
    private bool captureAnimation;
    private ICharacter? selectedCharacter;
    private readonly List<AnimationFrameData> frames = new();
    private bool IncludePositionalData = false;
    
    public AnimationTab(IFramework framework, ILogger<AnimationTab> logger, 
                        IClientState clientState, 
                        IObjectTable objectTable,
                        ExportUtil exportUtil,
                        Configuration config)
    {
        this.framework = framework;
        this.logger = logger;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.exportUtil = exportUtil;
        this.config = config;
        this.framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework1)
    {
        Capture();
    }

    public unsafe void Draw()
    {
        ICharacter[] objects;
        if (clientState.LocalPlayer != null)
        {
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValid() && obj.IsValidCharacterBase())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            // login/char creator produces "invalid" characters but are still usable I guess
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValidHuman())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }

        selectedCharacter ??= objects.FirstOrDefault() ?? clientState.LocalPlayer;
        
        ImGui.Text("Select Character");
        var preview = selectedCharacter != null ? clientState.GetCharacterDisplayText(selectedCharacter, config.PlayerNameOverride) : "None";
        using (var combo = ImRaii.Combo("##Character", preview))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(clientState.GetCharacterDisplayText(character, config.PlayerNameOverride)))
                    {
                        selectedCharacter = character;
                    }
                }
            }
        }
        
        if (selectedCharacter == null) return;
        if (ImGui.Checkbox("Capture Animation", ref captureAnimation))
        {
            if (captureAnimation)
            {
                logger.LogInformation("Capturing animation");
            }
            else
            {
                logger.LogInformation("Stopped capturing animation");
            }
        }
        
        Capture();
        
        var frameCount = frames.Count;
        ImGui.Text($"Frames: {frameCount}");
        if (ImGui.Button("Export"))
        {
            exportUtil.ExportAnimation(frames, IncludePositionalData);
        }
        
        ImGui.SameLine();
        ImGui.Checkbox("Include Positional Data", ref IncludePositionalData);
        
        if (ImGui.Button("Clear"))
        {
            frames.Clear();
        }
        
        ImGui.Separator();
        // render frames
        foreach (var frame in frames.ToArray())
        {
            if (ImGui.CollapsingHeader($"Frame: {frame.Time}##{frame.GetHashCode()}"))
            {
                foreach (var partial in frame.Skeleton.PartialSkeletons)
                {
                    ImGui.Text($"Partial: {partial.HandlePath}");
                    ImGui.Text($"Connected Bone Index: {partial.ConnectedBoneIndex}");
                    var poseData = partial.Poses.FirstOrDefault();
                    if (poseData == null) continue;
                    for (int i = 0; i < poseData.Pose.Count; i++)
                    {
                        var transform = poseData.Pose[i];
                        ImGui.Text($"Bone: {i} Scale: {transform.Scale} Rotation: {transform.Rotation} Translation: {transform.Translation}");
                    }
                }
            }
        }
    }

    private unsafe void Capture()
    {
        if (!captureAnimation) return;
        if (selectedCharacter == null) return;
        
        // 60fps
        if (frames.Count > 0 && DateTime.UtcNow - frames[^1].Time < TimeSpan.FromMilliseconds(100))
        {
            return;
        }
        
        var charPtr = (Character*)selectedCharacter.Address;
        if (charPtr == null)
        {
            logger.LogWarning("Character is null");
            captureAnimation = false;
            return;
        }
        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;
        if (cBase == null)
        {
            logger.LogWarning("CharacterBase is null");
            captureAnimation = false;
            return;
        }

        var skeleton = cBase->Skeleton;
        if (skeleton == null)
        {
            logger.LogWarning("Skeleton is null");
            captureAnimation = false;
            return;
        }

        var mSkele = new Skeleton.Skeleton(skeleton);
        var position = cBase->Position;
        var rotation = cBase->Rotation;
        var scale = cBase->Scale;
        var transform = new AffineTransform(scale, rotation, position);
        frames.Add(new AnimationFrameData(DateTime.UtcNow, mSkele, transform));
    }
    
    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }
}
