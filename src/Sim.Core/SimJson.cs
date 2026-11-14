using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sim.Core;

// Every stored form is read strictly: an unknown member or a number in place of an enum name is an error.
public static class SimJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
