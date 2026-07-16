namespace CrossETF.Terminal.UiShell.Reference.Tests.Display;

public sealed class SourceTextTestHelperTests
{
    public static IEnumerable<object[]> SupportedLineEndings()
    {
        yield return new object[]
        {
            "LF",
            "prefix\nBEGIN\nBuildTabs();\nLoadData();\nKey=\"Required\"\nEND\nsuffix"
        };
        yield return new object[]
        {
            "CRLF",
            "prefix\r\nBEGIN\r\nBuildTabs();\r\nLoadData();\r\nKey=\"Required\"\r\nEND\r\nsuffix"
        };
        yield return new object[]
        {
            "Mixed",
            "prefix\r\nBEGIN\nBuildTabs();\r\nLoadData();\nKey=\"Required\"\r\nEND\nsuffix"
        };
        yield return new object[]
        {
            "CR only",
            "prefix\rBEGIN\rBuildTabs();\rLoadData();\rKey=\"Required\"\rEND\rsuffix"
        };
    }

    [Theory]
    [MemberData(nameof(SupportedLineEndings))]
    public void StructureChecks_AcceptEverySupportedLineEnding(string _, string source)
    {
        string block = SourceTextTestHelper.Slice(source, "BEGIN\n", "\nEND");

        SourceTextTestHelper.RequireMarkersInOrder(block, "BuildTabs();", "LoadData();");
        SourceTextTestHelper.RequireContains(block, "Key=\"Required\"");
    }

    [Fact]
    public void Slice_RejectsMissingTargetStructure()
    {
        const string source = "prefix\nBEGIN\nBuildTabs();\nEND";

        Assert.Throws<InvalidOperationException>(
            () => SourceTextTestHelper.Slice(source, "MISSING\n", "\nEND"));
    }

    [Fact]
    public void RequireMarkersInOrder_RejectsReversedCalls()
    {
        const string source = "LoadData();\nBuildTabs();";

        Assert.Throws<InvalidOperationException>(
            () => SourceTextTestHelper.RequireMarkersInOrder(source, "BuildTabs();", "LoadData();"));
    }

    [Fact]
    public void RequireContains_RejectsDeletedKeyAttribute()
    {
        const string source = "<DataGrid MinHeight=\"150\" />";

        Assert.Throws<InvalidOperationException>(
            () => SourceTextTestHelper.RequireContains(source, "ToolTip=\"{Binding LastError}\""));
    }
}
