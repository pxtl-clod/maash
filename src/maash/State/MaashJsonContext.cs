namespace Maash.State;

using System.Text.Json;
using System.Text.Json.Serialization;
using Maash.Core;

[JsonSerializable(typeof(LoadedModuleEntry))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MaashRegistryState))]
[JsonSerializable(typeof(MaashModule))]
public partial class MaashJsonContext : JsonSerializerContext {
}
