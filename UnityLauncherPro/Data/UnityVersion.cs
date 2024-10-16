using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityLauncherPro
{
    public class UnityVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("stream")]
        [JsonConverter(typeof(UnityVersionStreamConverter))]
        public UnityVersionStream Stream { get; set; }
        [JsonPropertyName("releaseDate")]
        public DateTime ReleaseDate { get; set; }
    }
    
    public class UnityVersionStreamConverter : JsonConverter<UnityVersionStream>
    {
        public override UnityVersionStream Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string streamString = reader.GetString();
            if (Enum.TryParse<UnityVersionStream>(streamString, true, out var result))
            {
                return result;
            }
            throw new JsonException($"Unable to convert \"{streamString}\" to UnityVersionStream");
        }

        public override void Write(Utf8JsonWriter writer, UnityVersionStream value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString().ToUpper());
        }
    }
}