using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meddle.Plugin.Models.Composer;

public class IntPtrSerializer : JsonConverter<IntPtr>
{
    public override IntPtr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // do not read to IntPtr, it's not safe
        return IntPtr.Zero;
    }

    public override void Write(Utf8JsonWriter writer, IntPtr value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToInt64());
    }
}
