using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cngn.Sdk;

internal static class CngnJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
