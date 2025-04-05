using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meddle.Plugin.Models.Composer;

public class TransformSerializer : JsonConverter<Transform>
{
    public override Transform Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var translation = Vector3.Zero;
        var rotation = Quaternion.Identity;
        var scale = Vector3.One;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString();
            reader.Read();
            switch (propertyName)
            {
                case "Translation":
                    translation = new Vector3(reader.GetSingleNamed(), reader.GetSingleNamed(), reader.GetSingleNamed());
                    break;
                case "Rotation":
                    rotation = new Quaternion(reader.GetSingleNamed(), reader.GetSingleNamed(), reader.GetSingleNamed(), reader.GetSingleNamed());
                    break;
                case "Scale":
                    scale = new Vector3(reader.GetSingleNamed(), reader.GetSingleNamed(), reader.GetSingleNamed());
                    break;
            }
        }

        return new Transform(translation, rotation, scale);
    }
        
    public override void Write(Utf8JsonWriter writer, Transform value, JsonSerializerOptions options)
    {
        var translation = value.Translation;
        var rotation = value.Rotation;
        var scale = value.Scale;
        
        // make writer handle nan and infinity
            
        writer.WriteStartObject();
        writer.WriteStartArray("Translation");
        writer.WriteNumberValueNamed(translation.X);
        writer.WriteNumberValueNamed(translation.Y);
        writer.WriteNumberValueNamed(translation.Z);
        writer.WriteEndArray();
        writer.WriteStartArray("Rotation");
        writer.WriteNumberValueNamed(rotation.X);
        writer.WriteNumberValueNamed(rotation.Y);
        writer.WriteNumberValueNamed(rotation.Z);
        writer.WriteNumberValueNamed(rotation.W);
        writer.WriteEndArray();
        writer.WriteStartArray("Scale");
        writer.WriteNumberValueNamed(scale.X);
        writer.WriteNumberValueNamed(scale.Y);
        writer.WriteNumberValueNamed(scale.Z);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

public static class TransformSerializerFloatExtensions
{
    public static float GetSingleNamed(this ref Utf8JsonReader reader)
    {
        float value;
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (str == "NaN")
            {
                value = float.NaN;
            }
            else if (str == "Infinity")
            {
                value = float.PositiveInfinity;
            }
            else if (str == "-Infinity")
            {
                value = float.NegativeInfinity;
            }
            else
            {
                value = float.Parse(str);
            }
        }
        else
        {
            value = reader.GetSingle();
        }

        return value;
    }
    
    public static void WriteNumberValueNamed(this Utf8JsonWriter writer, float value)
    {
        // nan and infinity handling
        if (float.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (float.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (float.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }
}
