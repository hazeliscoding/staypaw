using Staypaw.Core;

namespace Staypaw.Tests;

public class FocusLogTests
{
    [Fact]
    public void The_log_writes_its_header_once_then_one_row_per_change()
    {
        var folder = Path.Combine(Path.GetTempPath(), "staypaw-test-" + Guid.NewGuid().ToString("N"));
        var log = new FocusLog(Path.Combine(folder, "focus-log.csv"));
        var thief = new WindowInfo { ExeName = "thief.exe", ProcessId = 42, Title = "Update ready", ClassName = "Popup", Visible = true };
        var entry = new LogEntry(new DateTime(2026, 10, 21, 15, 0, 0), new Classification(Verdict.Steal, "no click or switch asked for it"), thief, null);

        try
        {
            log.Add(entry);
            log.Add(entry);

            var lines = File.ReadAllLines(Path.Combine(folder, "focus-log.csv"));
            Assert.Equal(3, lines.Length);
            Assert.Equal("Time,Result,Process,PID,Path,WindowTitle,WindowClass,Visible,PreviousProcess,PreviousTitle,Reason", lines[0]);
            Assert.Equal("2026-10-21 15:00:00.000,Steal,thief.exe,42,,Update ready,Popup,yes,,,no click or switch asked for it", lines[1]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("a, \"b\"", "\"a, \"\"b\"\"\"")]
    public void Titles_are_quoted_and_never_become_spreadsheet_formulas(string title, string expected) =>
        Assert.Equal(expected, FocusLog.Csv(title));
}
