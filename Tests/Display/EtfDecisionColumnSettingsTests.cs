using CrossETF.Terminal.UiShell.Reference.Core.Services;
using System.Globalization;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public class EtfDecisionColumnSettingsTests
{
    [Fact]
    public void DefaultColumns_AreCoreSubsetAndContainRequiredColumns()
    {
        IReadOnlyList<string> defaults = EtfDecisionColumnSettings.DefaultVisibleKeys;

        Assert.True(defaults.Count < EtfDecisionColumnSettings.AllColumns.Count);
        foreach (string requiredKey in EtfDecisionColumnSettings.RequiredKeys)
        {
            Assert.Contains(requiredKey, defaults);
        }
    }

    [Fact]
    public void ParseVisibleColumns_UsesDefaultForMissingOrEmptyValue()
    {
        EtfDecisionColumnParseResult missing = EtfDecisionColumnSettings.ParseVisibleColumns(null);
        EtfDecisionColumnParseResult empty = EtfDecisionColumnSettings.ParseVisibleColumns(" ");

        Assert.True(missing.UsedDefault);
        Assert.True(empty.UsedDefault);
        Assert.Equal(EtfDecisionColumnSettings.DefaultVisibleKeys, missing.VisibleKeys);
        Assert.Equal(EtfDecisionColumnSettings.DefaultVisibleKeys, empty.VisibleKeys);
    }

    [Fact]
    public void ParseVisibleColumns_IgnoresUnknownKeysAndKeepsKnownKeys()
    {
        EtfDecisionColumnParseResult result = EtfDecisionColumnSettings.ParseVisibleColumns("code,name,price,unknown_key");

        Assert.True(result.IgnoredUnknown);
        Assert.Contains("price", result.VisibleKeys);
        Assert.DoesNotContain("unknown_key", result.VisibleKeys);
    }

    [Fact]
    public void ParseVisibleColumns_RestoresRequiredColumns()
    {
        EtfDecisionColumnParseResult result = EtfDecisionColumnSettings.ParseVisibleColumns("price,change");

        Assert.True(result.RestoredRequired);
        foreach (string requiredKey in EtfDecisionColumnSettings.RequiredKeys)
        {
            Assert.Contains(requiredKey, result.VisibleKeys);
        }
    }

    [Fact]
    public void ProjectRow_ChangesVisibleColumnCountWithoutChangingSourceRow()
    {
        string[] row = Enumerable.Range(0, EtfDecisionColumnSettings.AllColumns.Count)
            .Select(index => "v" + index.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        string[] projected = EtfDecisionColumnSettings.ProjectRow(row, new[] { "code", "name", "price" });

        Assert.Equal(EtfDecisionColumnSettings.AllColumns.Count, row.Length);
        Assert.Equal(5, projected.Length);
        Assert.Equal("v0", projected[0]);
        Assert.Equal("v1", projected[1]);
        Assert.Contains("v6", projected);
    }

    [Fact]
    public void ApplyPinnedSort_KeepsOriginalOrderWhenPinnedSymbolsAreEmpty()
    {
        string[] rows = ["159501", "159509", "159941"];

        IReadOnlyList<string> sorted = EtfDecisionColumnSettings.ApplyPinnedSort(rows, Array.Empty<string>(), row => row);

        Assert.Equal(rows, sorted);
    }

    [Fact]
    public void ApplyPinnedSort_MovesSinglePinnedSymbolToTop()
    {
        string[] rows = ["159501", "159509", "159941"];

        IReadOnlyList<string> sorted = EtfDecisionColumnSettings.ApplyPinnedSort(rows, ["159941"], row => row);

        Assert.Equal(["159941", "159501", "159509"], sorted);
    }

    [Fact]
    public void ApplyPinnedSort_UsesSavedOrderForMultiplePinnedSymbols()
    {
        string[] rows = ["159501", "159509", "159941", "513100"];

        IReadOnlyList<string> sorted = EtfDecisionColumnSettings.ApplyPinnedSort(rows, ["159941", "159509"], row => row);

        Assert.Equal(["159941", "159509", "159501", "513100"], sorted);
    }

    [Fact]
    public void ApplyPinnedSort_IgnoresUnknownPinnedSymbols()
    {
        string[] rows = ["159501", "159509"];

        IReadOnlyList<string> sorted = EtfDecisionColumnSettings.ApplyPinnedSort(rows, ["999999", "159509"], row => row);

        Assert.Equal(["159509", "159501"], sorted);
    }

    [Fact]
    public void ApplyPinnedSort_RestoresOriginalOrderAfterPinnedSymbolsAreCleared()
    {
        string[] rows = ["159501", "159509", "159941"];

        IReadOnlyList<string> sorted = EtfDecisionColumnSettings.ApplyPinnedSort(rows, EtfDecisionColumnSettings.ParsePinnedSymbols(string.Empty), row => row);

        Assert.Equal(rows, sorted);
    }

    [Fact]
    public void PinnedSymbols_RoundTripPreservesUserOrder()
    {
        string value = EtfDecisionColumnSettings.SerializePinnedSymbols(["159941", "159509", "159941", " "]);

        IReadOnlyList<string> parsed = EtfDecisionColumnSettings.ParsePinnedSymbols(value);

        Assert.Equal("159941,159509", value);
        Assert.Equal(["159941", "159509"], parsed);
    }

    [Fact]
    public void SignedNumberColorKeys_CoverEtfDecisionTableSignedColumns()
    {
        string[] expectedKeys =
        [
            "premium",
            "change",
            "daily_pnl",
            "total_pnl",
            "return_rate",
            "etf_drawdown",
            "index_drawdown",
            "index_change"
        ];

        foreach (string key in expectedKeys)
        {
            Assert.Contains(key, EtfDecisionColumnSettings.SignedNumberColorKeys);
            int sourceIndex = EtfDecisionColumnSettings.AllColumns.Single(column => column.Key == key).SourceIndex;
            Assert.True(EtfDecisionColumnSettings.UsesSignedNumberColorRule(sourceIndex));
        }
    }
}
