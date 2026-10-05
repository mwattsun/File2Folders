using System.Net;
using System.Text;
using System.Text.RegularExpressions;

const string Usage = """
    Turn a Netscape-format bookmarks file (as exported by Chrome, Edge or Firefox)
    into a folder tree of Internet Shortcut (.URL) files.

    Usage: File2Folders bookmarksfile [options]

    The tree is made next to the bookmarks file, in a folder named after it
    without its extension (bookmarks_10_5_26.html -> bookmarks_10_5_26\).
    Bookmark folders become folders and links become .URL files. Each file and
    folder gets its creation time from the bookmark's ADD_DATE and its modified
    time from LAST_MODIFIED (or ADD_DATE when there is none).

    Folders that already exist are reused, and .URL files that already exist are
    overwritten. Two links in the same folder whose cleaned-up titles match end
    up as one file (the later link wins); a warning is shown when that happens.

    Options:
      --no-dates    don't set file and folder times from the bookmarks file
      -h, --help    show this help

    """;

const int MaxNameLength = 127;

var linkRegex = new Regex(@"<A\s(?<attrs>[^>]*)>(?<text>.*?)</A>", RegexOptions.IgnoreCase);
var folderRegex = new Regex(@"<H3(?<attrs>[^>]*)>(?<text>.*?)</H3>", RegexOptions.IgnoreCase);
var attrRegex = new Regex(@"(?<name>[A-Z_]+)\s*=\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase);
var reservedRegex = new Regex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", RegexOptions.IgnoreCase);
char[] invalidChars = Path.GetInvalidFileNameChars();

Console.OutputEncoding = Encoding.UTF8;
return Run(args);

int Run(string[] args)
{
    bool setDates = true;
    string? file = null;

    foreach (var arg in args)
    {
        switch (arg)
        {
            case "-h" or "--help":
                Console.Write(Usage);
                return 0;
            case "--no-dates": setDates = false; break;
            default:
                if (arg.StartsWith('-') || file != null)
                {
                    Console.Error.WriteLine($"Unexpected argument: {arg}\n");
                    Console.Error.Write(Usage);
                    return 1;
                }
                file = arg;
                break;
        }
    }

    if (file == null)
    {
        Console.Error.Write(Usage);
        return 1;
    }
    file = Path.GetFullPath(file);
    if (!File.Exists(file))
    {
        Console.Error.WriteLine($"Bookmarks file not found: {file}");
        return 1;
    }

    string root = Path.ChangeExtension(file, null);
    if (!TryCreateFolder(root))
        return 1;

    // The folder we're in, plus the dates to give it once its contents are written
    // (adding a file to a folder updates its modified time).
    var folders = new Stack<(string Path, DateTime? Added, DateTime? Modified)>();
    folders.Push((root, null, null));
    var namesUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    int folderCount = 0, linkCount = 0, problems = 0;

    foreach (var rawLine in File.ReadLines(file, Encoding.UTF8))
    {
        var line = rawLine.Trim();

        if (line.StartsWith("<DT><A", StringComparison.OrdinalIgnoreCase))
        {
            var m = linkRegex.Match(line);
            var attrs = m.Success ? ParseAttributes(m.Groups["attrs"].Value) : null;
            if (attrs == null || !attrs.TryGetValue("HREF", out var url) || url.Length == 0)
            {
                Console.WriteLine($"Link line not parsed correctly: {line}");
                problems++;
                continue;
            }

            var name = CleanName(TagText(m.Groups["text"].Value));
            if (name.Length == 0)
                name = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0
                    ? CleanName(uri.Host) : "untitled";
            var path = Path.Combine(folders.Peek().Path, name + ".URL");

            if (!namesUsed.Add(path))
            {
                Console.WriteLine($"Duplicate name, earlier link replaced: {path}");
                problems++;
            }
            try
            {
                File.WriteAllText(path, $"[InternetShortcut]\r\nURL={url}\r\n", new UTF8Encoding(false));
                if (setDates)
                    SetDates(path, false, GetDate(attrs, "ADD_DATE"), GetDate(attrs, "LAST_MODIFIED"));
                linkCount++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.WriteLine($"Can't write {path}: {e.Message}");
                problems++;
            }
        }
        else if (line.StartsWith("<DT><H3", StringComparison.OrdinalIgnoreCase))
        {
            var m = folderRegex.Match(line);
            var attrs = m.Success ? ParseAttributes(m.Groups["attrs"].Value) : [];
            var name = m.Success ? CleanName(TagText(m.Groups["text"].Value)) : "";
            if (name.Length == 0)
                name = "untitled";

            var path = Path.Combine(folders.Peek().Path, name);
            if (!TryCreateFolder(path))
                return 1;
            folders.Push((path, GetDate(attrs, "ADD_DATE"), GetDate(attrs, "LAST_MODIFIED")));
            folderCount++;
        }
        else if (line.StartsWith("</DL>", StringComparison.OrdinalIgnoreCase))
        {
            // The last </DL> closes the top level list; the root folder keeps today's dates.
            if (folders.Count == 1)
                continue;
            var (path, added, modified) = folders.Pop();
            if (setDates)
            {
                try
                {
                    SetDates(path, true, added, modified);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Console.WriteLine($"Can't set dates on {path}: {e.Message}");
                    problems++;
                }
            }
        }
    }

    Console.WriteLine($"{folderCount} folders and {linkCount} links written to {root}"
        + (problems > 0 ? $" ({problems} problems, see above)" : ""));
    return problems > 0 ? 2 : 0;
}

bool TryCreateFolder(string path)
{
    try
    {
        Directory.CreateDirectory(path);
        return true;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"Can't make the folder {path}: {e.Message}");
        return false;
    }
}

Dictionary<string, string> ParseAttributes(string attrs)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (Match m in attrRegex.Matches(attrs))
        result[m.Groups["name"].Value] = WebUtility.HtmlDecode(m.Groups["value"].Value);
    return result;
}

// The text between the tags, with any inner tags dropped and entities like &amp; decoded.
string TagText(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", ""));

// ADD_DATE and LAST_MODIFIED are Unix times in seconds. A few exporters use
// milliseconds or microseconds, so scale anything too big to be seconds.
DateTime? GetDate(Dictionary<string, string> attrs, string key)
{
    if (!attrs.TryGetValue(key, out var text) || !long.TryParse(text, out var value) || value <= 0)
        return null;
    if (value > 100_000_000_000_000) value /= 1_000_000;
    else if (value > 100_000_000_000) value /= 1_000;
    try
    {
        return DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
    }
    catch (ArgumentOutOfRangeException)
    {
        return null;
    }
}

void SetDates(string path, bool isFolder, DateTime? added, DateTime? modified)
{
    var written = modified ?? added;
    if (isFolder)
    {
        if (added is { } a) Directory.SetCreationTimeUtc(path, a);
        if (written is { } w) Directory.SetLastWriteTimeUtc(path, w);
    }
    else
    {
        if (added is { } a) File.SetCreationTimeUtc(path, a);
        if (written is { } w) File.SetLastWriteTimeUtc(path, w);
    }
}

// Port of make_file_name(title, True) plus the extra fix-ups file2folders.py did.
// Returns "" if nothing usable is left.
string CleanName(string title)
{
    var name = title.Normalize(NormalizationForm.FormKC);
    name = Regex.Replace(name, @"\s", " ");            // tabs, newlines -> space
    name = Regex.Replace(name, @"[/:*?""<>|]", "").Trim();
    name = Regex.Replace(name, @"\.$", "");            // trailing dots are illegal
    name = name.Replace("..", "");
    name = name.Replace('[', '(').Replace(']', ')');
    if (name.Length > MaxNameLength)
        name = name[..(char.IsHighSurrogate(name[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength)];

    name = Regex.Replace(name, @"\.url$", "", RegexOptions.IgnoreCase);
    name = name.Replace("▶ ", "");                     // YouTube "playing" marker
    name = Regex.Replace(name, @"\A\(\d+\) ", "");     // leading (1), (2)... notification counts
    name = name.Replace("  ", " ");
    foreach (var c in invalidChars)                    // anything else Windows won't take, e.g. \
        name = name.Replace(c, '_');
    name = Regex.Replace(name, @"[. ]+$", "");         // no trailing dots or spaces
    if (reservedRegex.IsMatch(name))                   // CON, NUL, COM1... can't be used
        name += "_";
    return name;
}
