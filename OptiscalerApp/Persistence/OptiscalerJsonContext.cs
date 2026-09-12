using System.Text.Json.Serialization;
using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

/// <summary>
/// Provides source-generated metadata for the application's persistent JSON documents.
/// </summary>
/// <remarks>
/// Declaring root document types here makes serialization contracts explicit and avoids runtime
/// reflection, which is useful for startup performance and future trimming or native compilation.
/// </remarks>
[JsonSourceGenerationOptions(
                                WriteIndented = true,
                                PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                                GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ProfileCatalog))]
[JsonSerializable(typeof(OperationJournal))]
[JsonSerializable(typeof(GameCatalog))]
[JsonSerializable(typeof(AppConfiguration))]
internal partial class OptiscalerJsonContext : JsonSerializerContext;
