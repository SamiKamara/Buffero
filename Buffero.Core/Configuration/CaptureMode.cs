using System.Text.Json.Serialization;

namespace Buffero.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureMode>))]
public enum CaptureMode
{
    Window = 0,
    Display = 1
}
