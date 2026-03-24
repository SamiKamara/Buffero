using System.Text.Json.Serialization;

namespace Buffero.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureBackend>))]
public enum CaptureBackend
{
    Native = 0,
    Ffmpeg = 1
}
