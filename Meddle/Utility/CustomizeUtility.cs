using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin;
using ImGuiNET;
using Meddle.Plugin.Files;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Meddle.Plugin.Utility;

public class CustomizeResult
{
    public byte Height { get; set; }
    public byte Bust { get; set; }
    
    // TODO: Serialize vec3/4
    public Vector4 SkinColor { get; set; }
    public int SkinColorIndex { get; set; }
    public Vector4 LipColor { get; set; }
    public int LipColorIndex { get; set; }
    public Vector4 HairColor { get; set; }
    public int HairColorIndex { get; set; }
    public Vector4 HighlightsColor { get; set; }
    public int HighlightsColorIndex { get; set; }
    public Vector4 EyeColorRight { get; set; }
    public int EyeColorRightIndex { get; set; }
    public Vector4 EyeColorLeft { get; set; }
    public int EyeColorLeftIndex { get; set; }
    public bool Highlights { get; set; }
    public Race Race { get; set; }
    public Gender Gender { get; set; }
    public SubRace Clan { get; set; }

    public void Draw()
    {
        ImGui.Text($"Height: {Height}");
        ImGui.Text($"Bust: {Bust}");
        ImGui.Text($"Race: {Race}");
        ImGui.Text($"Clan: {Clan}");
        ImGui.Text($"Gender: {Gender}");

        DrawColorButton("Skin", SkinColor);
        ImGui.Text($"Skin Index: {SkinColorIndex}");
        DrawColorButton("Lip", LipColor);
        ImGui.Text($"Lip Index: {LipColorIndex}");
        
        DrawColorButton("Hair", HairColor);
        ImGui.Text($"Hair Index: {HairColorIndex}");
        DrawColorButton("Highlight", HighlightsColor);
        ImGui.Text($"Highlight Index: {HighlightsColorIndex}");
        var highlight = Highlights;
        ImGui.Checkbox("Use Hair Highlight", ref highlight);
        
        DrawColorButton("EyeR", EyeColorRight);
        ImGui.Text($"EyeR Index: {EyeColorRightIndex}");
        DrawColorButton("EyeL", EyeColorLeft);
        ImGui.Text($"EyeL Index: {EyeColorLeftIndex}");
        
    }

    private void DrawColorButton(string name, Vector4 color)
    {
        ImGui.Text(name);
        ImGui.SameLine();
        ImGui.ColorButton(name, color);
    }
}

public class CustomizeUtility
{
    // TODO: Might return to this but can't seem to get CustomizeArray data to actually change when changes are made
    // Might be tied to GameObject. If so is there a similar struct for the DrawObject?
    public static unsafe CustomizeResult ParseCustomize(CmpFile cmp, ref CustomizeArray* customizeArray)
    {
        var height = ParseByte(CustomizeIndex.Height, ref customizeArray);
        var bust = ParseByte(CustomizeIndex.BustSize, ref customizeArray);

        var skinColor = ParseSkinVector(ref customizeArray, cmp);
        var lipColor = ParseLipColor(ref customizeArray, cmp);
        var hairColor = ParseVector(CustomizeIndex.HairColor, ref customizeArray, cmp); // 3
        var highlightsColor = ParseVector(CustomizeIndex.HighlightsColor, ref customizeArray, cmp); // 3
        
        var eyeColorRight = ParseVector(CustomizeIndex.EyeColorRight, ref customizeArray, cmp); // 3
        var eyeColorLeft = ParseVector(CustomizeIndex.EyeColorLeft, ref customizeArray, cmp); // 3
        
        
        return new CustomizeResult
        {
            Height = height,
            Bust = bust,
            SkinColor = skinColor.color,
            SkinColorIndex = skinColor.index,
            
            LipColor = lipColor.color,
            LipColorIndex = lipColor.index,
            
            HairColor = hairColor.color,
            HairColorIndex = hairColor.index,
            
            Highlights = customizeArray->Highlights,
            HighlightsColor = highlightsColor.color,
            HighlightsColorIndex = highlightsColor.index,
            
            EyeColorRight = eyeColorRight.color,
            EyeColorRightIndex = eyeColorRight.index,
            EyeColorLeft = eyeColorLeft.color,
            EyeColorLeftIndex = eyeColorLeft.index,
            Race = customizeArray->Race,
            Clan = customizeArray->Clan,
            Gender = customizeArray->Gender
        };
    }
    
    private static unsafe (Vector4 color, int index) ParseSkinVector(ref CustomizeArray* customizeArray, CmpFile cmp)
    {
        var gv = customizeArray->Gender == Gender.Male ? 0 : 1;
        var index = ((int) customizeArray->Clan * 2 + gv) * 5 + 3;
        var offset = index << 8;
        
        return ParseVector(CustomizeIndex.SkinColor, ref customizeArray, cmp, offset);
    }

    private static unsafe (Vector4 color, int index) ParseLipColor(ref CustomizeArray* customizeArray, CmpFile cmp)
    {
        // TODO: This only matches dark lip colours, light should be 1024 offset but not sure how to identify when to use specific ones
        int offset = 512;

        return ParseVector(CustomizeIndex.LipColor, ref customizeArray, cmp, offset);
    }
    
    private static unsafe (Vector4 color, int index) ParseVector(CustomizeIndex idx, ref CustomizeArray* customizeArray, CmpFile cmp, int offset = 0)
    {
        var byteIdx = idx.ToByteAndMask().ByteIdx;
        var cIndex = customizeArray->Data[byteIdx];
        
        return (cmp.Colors[cIndex + offset], cIndex);
    }

    private static unsafe byte ParseByte(CustomizeIndex idx, ref CustomizeArray* customizeArray)
    {
        var byteIdx = idx.ToByteAndMask().ByteIdx;
        var data = customizeArray->Data[byteIdx];
        return data;
    }
}