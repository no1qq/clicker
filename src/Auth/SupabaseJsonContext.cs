using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace zolsi.cc;

internal sealed record HwidUpdatePayload(string hwid);
internal sealed record ConfigUpdatePayload(string config);
internal sealed record CreateRecordPayload(Guid user_id, string name, string data, double cps);
internal sealed record RenameRecordPayload(string name);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<AccountRow>))]
[JsonSerializable(typeof(List<ClickRecordRow>))]
[JsonSerializable(typeof(List<BlacklistedBuildRow>))]
[JsonSerializable(typeof(AccountRow))]
[JsonSerializable(typeof(ClickRecordRow))]
[JsonSerializable(typeof(BlacklistedBuildRow))]
[JsonSerializable(typeof(UserProfile))]
[JsonSerializable(typeof(List<int[]>))]
[JsonSerializable(typeof(HwidUpdatePayload))]
[JsonSerializable(typeof(ConfigUpdatePayload))]
[JsonSerializable(typeof(CreateRecordPayload))]
[JsonSerializable(typeof(RenameRecordPayload))]
internal sealed partial class SupabaseJsonContext : JsonSerializerContext
{
}