using System.Text.Json;

namespace Contracts;

public static class ContractJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
