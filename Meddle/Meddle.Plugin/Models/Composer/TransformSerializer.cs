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
                    translation = new Vector3(reader.GetSingle(), reader.GetSingle(), reader.GetSingle());
                    break;
                case "Rotation":
                    rotation = new Quaternion(reader.GetSingle(), reader.GetSingle(), reader.GetSingle(), reader.GetSingle());
                    break;
                case "Scale":
                    scale = new Vector3(reader.GetSingle(), reader.GetSingle(), reader.GetSingle());
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
            
        writer.WriteStartObject();
        writer.WriteStartArray("Translation");
        writer.WriteNumberValue(translation.X);
        writer.WriteNumberValue(translation.Y);
        writer.WriteNumberValue(translation.Z);
        writer.WriteEndArray();
        writer.WriteStartArray("Rotation");
        writer.WriteNumberValue(rotation.X);
        writer.WriteNumberValue(rotation.Y);
        writer.WriteNumberValue(rotation.Z);
        writer.WriteNumberValue(rotation.W);
        writer.WriteEndArray();
        writer.WriteStartArray("Scale");
        writer.WriteNumberValue(scale.X);
        writer.WriteNumberValue(scale.Y);
        writer.WriteNumberValue(scale.Z);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
