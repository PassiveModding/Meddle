using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using ImGuiNET;
using Meddle.Plugin.Services;

namespace Meddle.Plugin.Models.Customize
{
    public class Customize
    {
        public static Customize? FromCharacter(DalamudPluginInterface pi, Character character)
        {
            var customization = pi.GetIpcSubscriber<Character?, string?>("Glamourer.GetAllCustomizationFromCharacter").InvokeFunc(character);
            if (customization == null)
            {
                return null;
            }
            
            var db = Decompress(Convert.FromBase64String(customization), out var decompressedBytes);
            var cString = Encoding.UTF8.GetString(decompressedBytes);
            var name = character.Name.TextValue; 
            if (string.IsNullOrEmpty(name))
            {
                name = "unknown";
            }

            var path = Path.Combine(Plugin.TempDirectory, "Customize", $"{name}-v{db}.json");
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, cString);
            try
            {
                var customize = JsonSerializer.Deserialize<Customize>(cString);
                if (customize == null)
                {
                    throw new JsonException("Failed to deserialize customization.");
                }

                return customize;
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to deserialize customization. {cString}", e);
            }
        }
        
        private static byte Decompress(byte[] compressed, out byte[] decompressed)
        {
            var       ret              = compressed[0];
            using var compressedStream = new MemoryStream(compressed, 1, compressed.Length - 1);
            using var zipStream        = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream     = new MemoryStream();
            zipStream.CopyTo(resultStream);
            decompressed = resultStream.ToArray();
            return ret;
        }
        
        public Equipment Equipment { get; set; }
        
        public CustomizeClass Options { get; set; }
        public Parameters Parameters { get; set; }
        public Dictionary<string, Material> Materials { get; set; }

        public void Draw(StainService stainService)
        {
            using (var table = ImRaii.Table("Customize", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.NoClip))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableHeadersRow();
                
                
                ImGui.TableNextColumn();
                ImGui.Text("By the way");
                ImGui.TableNextColumn();
                // TODO: Use them
                ImGui.Text("These are displayed but not actually used currently.");
                
                Equipment.MainHand?.Draw("MainHand", stainService);
                Equipment.OffHand?.Draw("OffHand", stainService);
                Equipment.Head?.Draw("Head", stainService);
                Equipment.GearPiece?.Draw("GearPiece", stainService);
                Equipment.Hands?.Draw("Hands", stainService);
                Equipment.Legs?.Draw("Legs", stainService);
                Equipment.Feet?.Draw("Feet", stainService);
                Equipment.Ears?.Draw("Ears", stainService);
                Equipment.Neck?.Draw("Neck", stainService);
                Equipment.Wrists?.Draw("Wrists", stainService);
                Equipment.RFinger?.Draw("RFinger", stainService);
                Equipment.LFinger?.Draw("LFinger", stainService);
                //Parameters.MuscleTone?.Draw();
                
                ImGui.TableNextColumn();
                ImGui.Text("By the way");
                ImGui.TableNextColumn();
                ImGui.Text("These 'may' not be entirely correct compared to what is reported by glamourer. Not sure why.");
                
                Parameters.SkinDiffuse?.Draw("SkinDiffuse");
                //Parameters.SkinSpecular?.Draw("SkinSpecular");
                Parameters.HairDiffuse?.Draw("HairDiffuse");
                //Parameters.HairSpecular?.Draw("HairSpecular");
                Parameters.HairHighlight?.Draw("HairHighlight");
                Parameters.LeftEye?.Draw("LeftEye");
                Parameters.RightEye?.Draw("RightEye");
                //Parameters.FeatureColor?.Draw("FeatureColor");
                Parameters.LipDiffuse?.Draw("LipDiffuse");
            }
        }
    }
    
    public class CustomizeClass
    {
        public long ModelId { get; set; }
        public Option? Race { get; set; }
        public Option? Gender { get; set; }
        public Option? BodyType { get; set; }
        public Option? Height { get; set; }
        public Option? Clan { get; set; }
        public Option? Face { get; set; }
        public Option? Hairstyle { get; set; }
        public Option? Highlights { get; set; }
        public Option? SkinColor { get; set; }
        public Option? EyeColorRight { get; set; }
        public Option? HairColor { get; set; }
        public Option? HighlightsColor { get; set; }
        public Option? FacialFeature1 { get; set; }
        public Option? FacialFeature2 { get; set; }
        public Option? FacialFeature3 { get; set; }
        public Option? FacialFeature4 { get; set; }
        public Option? FacialFeature5 { get; set; }
        public Option? FacialFeature6 { get; set; }
        public Option? FacialFeature7 { get; set; }
        public Option? LegacyTattoo { get; set; }
        public Option? TattooColor { get; set; }
        public Option? Eyebrows { get; set; }
        public Option? EyeColorLeft { get; set; }
        public Option? EyeShape { get; set; }
        public Option? SmallIris { get; set; }
        public Option? Nose { get; set; }
        public Option? Jaw { get; set; }
        public Option? Mouth { get; set; }
        public Option? Lipstick { get; set; }
        public Option? LipColor { get; set; }
        public Option? MuscleMass { get; set; }
        public Option? TailShape { get; set; }
        public Option? BustSize { get; set; }
        public Option? FacePaint { get; set; }
        public Option? FacePaintReversed { get; set; }
        public Option? FacePaintColor { get; set; }
        public Wetness? Wetness { get; set; }
    }
    
    public class Material
    {
        public bool Revert { get; set; }
        public double DiffuseR { get; set; }
        public double DiffuseG { get; set; }
        public double DiffuseB { get; set; }
        public double SpecularR { get; set; }
        public double SpecularG { get; set; }
        public double SpecularB { get; set; }
        public double SpecularA { get; set; }
        public double EmissiveR { get; set; }
        public double EmissiveG { get; set; }
        public double EmissiveB { get; set; }
        public double Gloss { get; set; }
        public bool Enabled { get; set; }
    } 
        
    public class Wetness
    {
        public bool Value { get; set; }
    }

    public class Option
    {
        public long Value { get; set; }
    }

    public class Equipment
    {
        public GearPiece? MainHand { get; set; }
        public GearPiece? OffHand { get; set; }
        public GearPiece? Head { get; set; }
        public GearPiece? GearPiece { get; set; }
        public GearPiece? Hands { get; set; }
        public GearPiece? Legs { get; set; }
        public GearPiece? Feet { get; set; }
        public GearPiece? Ears { get; set; }
        public GearPiece? Neck { get; set; }
        public GearPiece? Wrists { get; set; }
        public GearPiece? RFinger { get; set; }
        public GearPiece? LFinger { get; set; }
    }

    public class GearPiece
    {
        public long ItemId { get; set; }
        public long Stain { get; set; }
        
        public void Draw(string name, StainService stainService)
        {
            ImGui.TableNextColumn();
            ImGui.Text(name);
            ImGui.TableNextColumn();
            ImGui.Text($"Item: {ItemId}");
            var stain = stainService.GetStain((byte)Stain);
            if (stain.color != Vector4.Zero)
            {
                ImGui.SameLine();
                ImGui.Text(stain.name);
                ImGui.SameLine();
                ImGui.ColorButton("Stain", stain.color);
            }
        }
    }
    
    public class Parameters
    {
        public MuscleTone? MuscleTone { get; set; }
        public CustomizeColor? SkinDiffuse { get; set; }
        //public CustomizeColor? SkinSpecular { get; set; }
        public CustomizeColor? HairDiffuse { get; set; }
        //public CustomizeColor? HairSpecular { get; set; }
        public CustomizeColor? HairHighlight { get; set; }
        public CustomizeColor? LeftEye { get; set; }
        public CustomizeColor? RightEye { get; set; }
        // CustomizeColor? FeatureColor { get; set; }
        public CustomizeColor? LipDiffuse { get; set; }
        
        public static readonly Vector4 DefaultHairColor          = new Vector4(130, 64,  13,  255) / new Vector4(255);
        public static readonly Vector4 DefaultHairHighlightColor = new Vector4(77,  126, 240, 255) / new Vector4(255);
        public static readonly Vector4 DefaultIrisColor          = new Vector4(21,  176, 172, 255) / new Vector4(255);
        public static readonly Vector4 DefaultSkinColor          = new Vector4(234, 183, 161, 255) / new Vector4(255);
        public static readonly Vector4 DefaultLipColor           = new Vector4(120, 69,  104, 153) / new Vector4(255);
        
        public static Parameters Default()
        {
            return new Parameters
            {
                MuscleTone = new MuscleTone
                {
                    Percentage = 1d
                },
                SkinDiffuse = new CustomizeColor(DefaultSkinColor),
                HairDiffuse = new CustomizeColor(DefaultHairColor),
                HairHighlight = new CustomizeColor(DefaultHairHighlightColor),
                LeftEye = new CustomizeColor(DefaultIrisColor),
                RightEye = new CustomizeColor(DefaultIrisColor),
                LipDiffuse = new CustomizeColor(DefaultLipColor),
            };
        }

        public Parameters WithDefault()
        {
            var defaultParams = Default();

            return new Parameters
            {
                MuscleTone = MuscleTone ?? defaultParams.MuscleTone,
                SkinDiffuse = SkinDiffuse ?? defaultParams.SkinDiffuse,
                HairDiffuse = HairDiffuse ?? defaultParams.HairDiffuse,
                HairHighlight = HairHighlight ?? defaultParams.HairHighlight,
                LeftEye = LeftEye ?? defaultParams.LeftEye,
                RightEye = RightEye ?? defaultParams.RightEye,
                LipDiffuse = LipDiffuse ?? defaultParams.LipDiffuse,
            };
        }
    }

    public class CustomizeColor
    {
        public CustomizeColor(){}
        public CustomizeColor(Vector4 color)
        {
            Red = color.X;
            Green = color.Y;
            Blue = color.Z;
            Alpha = color.W;
        }
        
        public float Red { get; set; }
        public float Green { get; set; }
        public float Blue { get; set; }
        public float? Alpha { get; set; }
        
        public Vector4 ToVector4()
        {
            return new Vector4(Red, Green, Blue, Alpha ?? 1);
        }
        
        public Vector3 ToVector3()
        {
            return new Vector3(Red, Green, Blue);
        }
        
        public Vector4 ToXivVector4()
        {
            return new FFXIVClientStructs.FFXIV.Common.Math.Vector4(Square(Red), Square(Green), Square(Blue), Alpha ?? 1);
        }
        
        private static float Square(float x)
            => x < 0 ? -x * x : x * x;
        
        public void Draw(string name)
        {
            ImGui.TableNextColumn();
            ImGui.Text(name);
            ImGui.TableNextColumn();
            if (Alpha == null)
            {
                var v3 = ToVector3();
                if (ImGui.ColorEdit3($"{name}##edit", ref v3))
                {
                    Red = v3.X;
                    Green = v3.Y;
                    Blue = v3.Z;
                }
            }
            else
            {
                var v4 = ToVector4();
                if (ImGui.ColorEdit4($"{name}##edit", ref v4))
                {
                    Red = v4.X;
                    Green = v4.Y;
                    Blue = v4.Z;
                    Alpha = v4.W;
                }
            }
        }
    }

    public class MuscleTone
    {
        public double Percentage { get; set; }
    }
}
