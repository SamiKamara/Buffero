using System.Text.Json.Serialization;

namespace Buffero.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<BufferActivationMode>))]
public enum BufferActivationMode
{
    Automatic = 0,
    HotkeyToggle = 1
}
