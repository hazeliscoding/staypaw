using System.Text;

namespace Staypaw.Core;

// Ported from NoFocusSteal's FocusLog.cs at 279e9cd (MIT); see THIRD-PARTY-NOTICES.md. The columns are kept,
// so logs from both tools can be compared; the Result column holds Staypaw's verdicts.

internal sealed record LogEntry(DateTime Time, Classification Classification, WindowInfo Window, WindowInfo? Previous, string Note = "");

/// <summary>Appends every focus change to a CSV file. Window titles are stored; keys never are.</summary>
internal sealed class FocusLog(string path)
{
    private const long MaxFileBytes = 5 * 1024 * 1024;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Staypaw", "focus-log.csv");

    public void Add(LogEntry e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new FileInfo(path);
            if (file.Exists && file.Length > MaxFileBytes)
            {
                file.MoveTo(path + ".old", overwrite: true);
            }

            var header = !File.Exists(path);
            using var w = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            if (header)
            {
                w.WriteLine("Time,Result,Process,PID,Path,WindowTitle,WindowClass,Visible,PreviousProcess,PreviousTitle,Reason");
            }

            w.WriteLine(string.Join(",",
                e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                e.Classification.Verdict,
                Csv(e.Window.ExeName),
                e.Window.ProcessId,
                Csv(e.Window.ExePath),
                Csv(e.Window.Title),
                Csv(e.Window.ClassName),
                e.Window.Visible ? "yes" : "no",
                Csv(e.Previous?.ExeName ?? ""),
                Csv(e.Previous?.Title ?? ""),
                Csv(e.Note.Length > 0 ? e.Classification.Reason + " " + e.Note : e.Classification.Reason)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is best effort: a locked or full disk must not stop the watcher.
            System.Diagnostics.Trace.WriteLine("Staypaw: log write failed: " + ex.Message);
        }
    }

    internal static string Csv(string s)
    {
        // Window titles are arbitrary text; keep spreadsheet apps from treating one as a formula.
        if (s.Length > 0 && "=+-@".Contains(s[0]))
        {
            s = "'" + s;
        }

        return s.AsSpan().IndexOfAny(",\"\n\r") >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
