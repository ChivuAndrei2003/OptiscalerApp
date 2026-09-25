using System.Text.Json;
using System.Text.RegularExpressions;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.Management;

/// <summary>
///     Caches the OptiScaler wiki compatibility tables and refreshes them at most once a day. The wiki is
///     community-edited and incomplete, so a missing entry means "untested", not "unsupported".
/// </summary>
public sealed class CompatibilityListService(IAppPaths paths, HttpClient client)
{
    public const string WikiUrl = "https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List";

    // Raw wiki content is served from a static host and does not count against the GitHub API rate limit.
    private const string SourceUrl =
        "https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/Compatibility-List.md";

    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly AtomicJsonFile<CompatibilityCatalog> _store = new(
                                                                       Path.Combine(paths.RootDirectory,
                                                                                    "compatibility.json"),
                                                                       OptiscalerJsonContext.Default
                                                                           .CompatibilityCatalog,
                                                                       ValidateCatalog);

    private CompatibilityIndex? _index;
    private DateTimeOffset _lastFailureUtc = DateTimeOffset.MinValue;

    /// <summary>The list saved by an earlier refresh, without any network access.</summary>
    public Task<CompatibilityIndex> GetCachedIndex_Async(CancellationToken cancellationToken = default)
    {
        return GetIndex_Async(false, false, cancellationToken);
    }

    /// <summary>Returns the cached list, refreshing it first when it is stale. Never throws for network errors.</summary>
    public Task<CompatibilityIndex> GetIndex_Async(bool forceRefresh = false,
                                                   CancellationToken cancellationToken = default)
    {
        return GetIndex_Async(true, forceRefresh, cancellationToken);
    }

    private async Task<CompatibilityIndex> GetIndex_Async(bool allowNetwork, bool forceRefresh,
                                                          CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_index is null)
                try
                {
                    if (await _store.LoadJsonFile_Async(cancellationToken).ConfigureAwait(false) is { } cached)
                        _index = new CompatibilityIndex(cached);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                               or InvalidDataException)
                {
                    // A damaged cache is replaced by the next successful download.
                }

            var now = DateTimeOffset.UtcNow;
            var stale = _index is null || now - _index.FetchedAtUtc > MaxAge;

            // A failed download is not retried for a while, even when forced: offline, every "Check updates"
            // would otherwise wait for another request to fail. A forced refresh after a success still runs.
            if (allowNetwork && (forceRefresh || stale) && now - _lastFailureUtc > RetryDelay)
                _lastFailureUtc = await Refresh_Async(cancellationToken).ConfigureAwait(false)
                    ? DateTimeOffset.MinValue
                    : now;

            return _index ?? CompatibilityIndex.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <returns>False when the list could not be downloaded or parsed; the cached list stays in use.</returns>
    private async Task<bool> Refresh_Async(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var markdown = await client.GetStringAsync(SourceUrl, timeout.Token).ConfigureAwait(false);
            var entries = CompatibilityListParser.Parse(markdown);

            // An empty parse usually means the page layout changed; keep the last good list instead.
            if (entries.Count == 0) return false;

            var catalog = new CompatibilityCatalog { FetchedAtUtc = DateTimeOffset.UtcNow, Entries = entries };
            _index = new CompatibilityIndex(catalog);

            try
            {
                await _store.SaveJsonFile_Async(catalog, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unwritable cache; the downloaded list is still used for this session.
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false; // Timed out.
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return false; // Offline or the page is unavailable.
        }
    }

    private static void ValidateCatalog(CompatibilityCatalog catalog)
    {
        if (catalog.SchemaVersion != CompatibilityCatalog.CurrentSchemaVersion || catalog.Entries is null ||
            catalog.Entries.Any(e => e is null || string.IsNullOrWhiteSpace(e.GameName) || e.MentionedProxies is null))
            throw new InvalidDataException("Invalid compatibility cache.");
    }
}

/// <summary>Looks up entries by game name: exact after normalization, then a conservative token match.</summary>
public sealed class CompatibilityIndex
{
    public static readonly CompatibilityIndex Empty = new(new CompatibilityCatalog());

    private const double MinimumScore = 0.7;
    private const double MinimumLead = 0.15;

    private static readonly HashSet<string> IgnoredWords =
    [
        "the", "a", "an", "of", "and", "edition", "deluxe", "ultimate", "gold", "goty", "complete", "definitive",
        "digital", "standard", "premium", "enhanced"
    ];

    private readonly Dictionary<string, CompatibilityEntry> _byName = new(StringComparer.Ordinal);
    private readonly List<(CompatibilityEntry Entry, HashSet<string> Words, HashSet<string> Numbers)> _byWords = [];

    public CompatibilityIndex(CompatibilityCatalog catalog)
    {
        FetchedAtUtc = catalog.FetchedAtUtc;
        Count = catalog.Entries.Count;

        foreach (var entry in catalog.Entries)
        {
            // The main table comes first on the page, so its row wins over mod-specific tables. Duplicates are
            // left out of the fuzzy candidates too, where two equal rows would otherwise look ambiguous.
            if (!_byName.TryAdd(NormalizeName(entry.GameName), entry)) continue;

            var words = Words(entry.GameName);
            _byWords.Add((entry, words, Numbers(words)));
        }
    }

    public DateTimeOffset FetchedAtUtc { get; }

    public int Count { get; }

    public CompatibilityEntry? Find(string? gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return null;

        if (_byName.TryGetValue(NormalizeName(gameName), out var exact)) return exact;

        var words = Words(gameName);

        if (words.Count == 0) return null;

        var numbers = Numbers(words);
        CompatibilityEntry? best = null;
        double bestScore = 0, secondScore = 0;

        foreach (var (entry, candidate, candidateNumbers) in _byWords)
        {
            // "Dying Light" must never match "Dying Light 2".
            if (!numbers.SetEquals(candidateNumbers)) continue;

            var shared = words.Count(candidate.Contains);
            var score = (double)shared / (words.Count + candidate.Count - shared);

            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                best = entry;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }

        return bestScore >= MinimumScore && bestScore - secondScore >= MinimumLead ? best : null;
    }

    internal static string NormalizeName(string name)
    {
        return string.Join(' ', Regex.Split(name.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
                               .Where(part => part.Length > 0));
    }

    private static HashSet<string> Words(string name)
    {
        return NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !IgnoredWords.Contains(word)).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Numbers(HashSet<string> words)
    {
        return words.Where(word => word.All(char.IsDigit) || Regex.IsMatch(word, "^[ivx]+$"))
            .ToHashSet(StringComparer.Ordinal);
    }
}

/// <summary>Reads the Markdown tables on the wiki's Compatibility-List page.</summary>
public static class CompatibilityListParser
{
    private const string WikiRoot = "https://github.com/optiscaler/OptiScaler/wiki/";

    private static readonly Regex Link = new(@"\[([^\]]*)\]\(([^)]*)\)", RegexOptions.CultureInvariant);

    private static readonly Regex ProxyMention =
        new($@"\b({string.Join('|', GameInstallationService.ProxyNames.Select(Regex.Escape))})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<CompatibilityEntry> Parse(string markdown)
    {
        var entries = new List<CompatibilityEntry>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var header = SplitRow(lines[i]);

            // Commented-out row templates ("| GAME NAME | ...") are not headers, so they are never parsed.
            if (header.Count < 3 || !header[0].Equals("Game", StringComparison.OrdinalIgnoreCase) ||
                !header[1].StartsWith("Compatibility", StringComparison.OrdinalIgnoreCase))
                continue;

            var inputs = header.FindIndex(cell => cell.Contains("Inputs", StringComparison.OrdinalIgnoreCase));
            var patcher = header.FindIndex(cell => cell.Contains("OptiPatcher", StringComparison.OrdinalIgnoreCase));
            var notes = header.FindIndex(cell => cell.StartsWith("Notes", StringComparison.OrdinalIgnoreCase));

            // Skip the "| --- |" separator, then read rows until the table ends.
            for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
            {
                var cells = SplitRow(lines[i]);

                if (cells.Count < 2 || cells[0].Length == 0) continue;

                string Cell(int index) { return index >= 0 && index < cells.Count ? cells[index] : ""; }

                var link = Link.Match(cells[0]);
                var name = link.Success ? link.Groups[1].Value : cells[0];
                var slug = link.Success ? link.Groups[2].Value.Trim() : null;
                var rawNotes = Cell(notes);
                entries.Add(new CompatibilityEntry
                {
                    GameName = Clean(name),
                    Status = ParseStatus(cells[1]),
                    Inputs = Clean(Cell(inputs)),
                    OptiPatcherSupported = Cell(patcher).Length > 0,
                    Notes = Clean(rawNotes),
                    PageUrl = string.IsNullOrEmpty(slug) ? null
                        : slug.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? slug
                        : WikiRoot + slug.TrimStart('/'),
                    MentionedProxies = ProxyMention.Matches(rawNotes)
                        .Select(match => match.Value.ToLowerInvariant()).Distinct().ToList()
                });
            }
        }

        return entries.Where(entry => entry.GameName.Length > 0).ToList();
    }

    private static List<string> SplitRow(string line)
    {
        var trimmed = line.Trim();

        if (!trimmed.StartsWith('|')) return [];

        return trimmed.Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static CompatibilityStatus ParseStatus(string cell)
    {
        if (cell.Contains('❌')) return CompatibilityStatus.NotWorking;
        if (cell.Contains("💥", StringComparison.Ordinal)) return CompatibilityStatus.WorkingOnSingleOs;
        if (cell.Contains('✅') || cell.Contains('✔')) return CompatibilityStatus.Working;

        return CompatibilityStatus.Unconfirmed;
    }

    private static string Clean(string text)
    {
        text = Link.Replace(text, "$1");
        text = Regex.Replace(text, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        text = text.Replace("**", "").Replace("`", "");

        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
