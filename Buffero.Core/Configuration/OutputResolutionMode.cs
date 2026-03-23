using System.Text.Json.Serialization;

namespace Buffero.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<OutputResolutionMode>))]
public enum OutputResolutionMode
{
    Native = 0,
    Max1080p = 1,
    Max720p = 2
}
