using System.Text.Json.Serialization;

namespace Buffero.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<QualityInputMode>))]
public enum QualityInputMode
{
    Crf,
    Bitrate
}
