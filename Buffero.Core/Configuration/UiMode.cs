using System.Text.Json.Serialization;

namespace Buffero.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<UiMode>))]
public enum UiMode
{
    Default = 0,
    Advanced = 1
}
