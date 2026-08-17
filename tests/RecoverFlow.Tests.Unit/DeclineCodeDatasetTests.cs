using System.Reflection;
using System.Text.Json;
using RecoverFlow.Domain;
using RecoverFlow.Domain.Services;

namespace RecoverFlow.Tests.Unit;

/// <summary>
/// docs/data/stripe-decline-codes.json is published as a machine-readable dataset, so
/// other people's code can end up depending on it. It is generated from
/// <see cref="DeclineCodeClassifier"/> by scripts/build_decline_dataset.py, and nothing
/// re-runs that generator automatically. These tests are the tripwire: change a set in
/// the classifier without regenerating and the build fails here, rather than the site
/// quietly serving a classification the product no longer applies.
///
/// Fix by running: python scripts/build_decline_dataset.py
/// </summary>
public class DeclineCodeDatasetTests
{
    private static readonly IReadOnlyList<DatasetRow> Dataset = LoadDataset();

    [Fact]
    public void Dataset_lists_exactly_the_codes_the_classifier_knows()
    {
        var classifier = new SortedSet<string>(
            PrivateSet("RetryBlockedByStripe")
                .Concat(PrivateSet("NotWorthRetrying"))
                .Concat(PrivateSet("SoftDeclineCodes")));

        var published = new SortedSet<string>(Dataset.Select(r => r.Code));

        Assert.Equal(classifier, published);
    }

    [Fact]
    public void Blocked_by_stripe_column_matches_the_classifier()
    {
        var blocked = new SortedSet<string>(PrivateSet("RetryBlockedByStripe"));
        var published = new SortedSet<string>(
            Dataset.Where(r => r.BlockedByStripe).Select(r => r.Code));

        Assert.Equal(blocked, published);

        foreach (var row in Dataset)
            Assert.Equal(row.BlockedByStripe, DeclineCodeClassifier.IsBlockedByStripe(row.Code));
    }

    [Fact]
    public void Classification_column_matches_what_the_classifier_returns()
    {
        foreach (var row in Dataset)
            Assert.Equal(Slug(DeclineCodeClassifier.Classify(row.Code)), row.Classification);
    }

    /// <summary>
    /// RequiresCardUpdate is a pattern match rather than a set, so it cannot be read by
    /// reflection. Every code it accepts today is inside the three sets, and the test
    /// above pins the dataset to exactly those sets, so checking each published row is
    /// equivalent to checking the whole predicate.
    /// </summary>
    [Fact]
    public void Requires_card_update_column_matches_the_classifier()
    {
        foreach (var row in Dataset)
            Assert.Equal(row.RequiresCardUpdate, DeclineCodeClassifier.RequiresCardUpdate(row.Code));
    }

    [Fact]
    public void Every_row_says_what_to_do_about_the_code()
    {
        foreach (var row in Dataset)
            Assert.False(string.IsNullOrWhiteSpace(row.RecommendedAction),
                $"{row.Code} has no recommended_action");
    }

    private static string Slug(DeclineType type) => type switch
    {
        DeclineType.HardDecline => "hard_decline",
        DeclineType.SoftDecline => "soft_decline",
        _ => "unknown",
    };

    private static HashSet<string> PrivateSet(string field)
    {
        var f = typeof(DeclineCodeClassifier).GetField(
            field, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(f is not null,
            $"DeclineCodeClassifier.{field} was renamed or removed. "
            + "scripts/build_decline_dataset.py reads it by name and needs the same fix.");
        return (HashSet<string>)f!.GetValue(null)!;
    }

    private static IReadOnlyList<DatasetRow> LoadDataset()
    {
        var path = FindRepoFile(Path.Combine("docs", "data", "stripe-decline-codes.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("codes").EnumerateArray().Select(e => new DatasetRow(
            e.GetProperty("code").GetString()!,
            e.GetProperty("blocked_by_stripe").GetBoolean(),
            e.GetProperty("classification").GetString()!,
            e.GetProperty("requires_card_update").GetBoolean(),
            e.GetProperty("recommended_action").GetString())).ToList();
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"{relative} not found above {AppContext.BaseDirectory}. "
            + "Run: python scripts/build_decline_dataset.py");
    }

    private sealed record DatasetRow(
        string Code,
        bool BlockedByStripe,
        string Classification,
        bool RequiresCardUpdate,
        string? RecommendedAction);
}
