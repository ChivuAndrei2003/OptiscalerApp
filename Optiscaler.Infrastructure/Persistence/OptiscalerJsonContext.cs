using System.Text.Json.Serialization;
using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Persistence;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GameCatalog))]
[JsonSerializable(typeof(AppConfiguration))]
internal partial class OptiscalerJsonContext : JsonSerializerContext;