// Shared JSON options used when serializing artifact/graph into a jsonb column.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticSdlc.Modules.Pipeline.Persistence;

internal static class PersistenceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
