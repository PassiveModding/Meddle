using System.Numerics;

namespace Meddle.Utils.Materials.Shaders;

public class Structs
{
    public struct MaterialParameter
    {
        public Vector4[] Values; // Array of 19 float4 values
        
        public MaterialParameter(ref SpanBinaryReader reader)
        {
            Values = new Vector4[19];
            for (int i = 0; i < 19; i++)
            {
                Values[i] = reader.Read<Vector4>();
            }
        }
    }
    
    public struct CharacterCustomization
    {
        public Vector4 SkinColor;
        public Vector4 SkinFresnelValue0;
        public Vector4 LipColor;
        public Vector4 MainColor;
        public Vector3 HairFresnelValue0;
        public Vector4 MeshColor;
        public Vector4 LeftColor;
        public Vector4 RightColor;
        public Vector3 OptionColor;
    }
    
    public struct DecalColor
    {
        public Vector4 Color;
    }
}
