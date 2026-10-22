using System.Globalization;
using System.Text;
using System.Text.Json;
using Core;

namespace Editing;

/// A game file being edited. It keeps the file's own text and changes only the part being edited, so comments,
/// key order and formatting everywhere else stay exactly as written. Paths name a value: ["zones", "deck", "anchor"],
/// ["turn", 0] (keys are text, list positions are whole numbers).
public class GameDocument
{
    const string Indent = "\t";

    /// Where a value sits in the text: from Start to End, its entry (its key, for an object's member) from
    /// MemberStart, and its key between KeyStart and KeyEnd (-1 for a list item).
    record Span(int Start, int End, int MemberStart, int KeyStart, int KeyEnd);

    public string Path { get; set; } = "";
    public string Text { get; private set; } = "";
    /// The file as plain values (null if it isn't valid JSONC; then ParseError says why).
    public object? Data { get; private set; }
    public string ParseError { get; private set; } = "";

    readonly Dictionary<string, Span> spans = [];
    int at;

    public static GameDocument Open(GameFiles files, string path)
    {
        GameDocument document = new() { Path = path };
        document.SetText(files.Read(path));
        return document;
    }

    /// Writes the text to the file; false if it couldn't.
    public bool Save(GameFiles files)
    {
        try
        {
            File.WriteAllText(files.FullPath(Path), Text);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void SetText(string newText)
    {
        Text = newText;
        try
        {
            Data = GameRules.Parse(Text);
            ParseError = "";
            IndexText();
        }
        catch (JsonException error)
        {
            Data = null;
            ParseError = $"line {error.LineNumber + 1}: {error.Message.Split(" LineNumber:")[0].Split(" Path:")[0]}";
            spans.Clear();
        }
    }

    public bool Has(List<object> path) => spans.ContainsKey(Key(path));

    /// Where the value's entry starts in the text (its key, for an object's member).
    public int OffsetOf(List<object> path) => Has(path) ? spans[Key(path)].MemberStart : 0;

    public object? GetValue(List<object> path, object? fallback = null)
    {
        object? value = Data;
        foreach (object step in path)
        {
            if (value is Dictionary<string, object?> dictionary && step is string key && dictionary.TryGetValue(key, out object? member))
            {
                value = member;
            }
            else if (value is List<object?> list && step is int index && index >= 0 && index < list.Count)
            {
                value = list[index];
            }
            else
            {
                return fallback;
            }
        }
        return value;
    }

    /// Sets a value, adding it (and any missing objects above it) if it isn't there yet.
    public void SetValue(List<object> path, object? value)
    {
        // Text that isn't valid JSONC has no values to find, so there's nowhere to put this one.
        if (Data == null)
        {
            return;
        }
        if (Has(path))
        {
            if (OneLine(GetValue(path)) != OneLine(value))
            {
                Span span = spans[Key(path)];
                Replace(span.Start, span.End, Format(value, IndentAt(span.Start)));
            }
            return;
        }
        List<object> parent = path[..^1];
        object key = path[^1];
        if (!Has(parent))
        {
            SetValue(parent, key is string name ? new Dictionary<string, object?> { [name] = value } : new List<object?> { value });
            return;
        }
        if (GetValue(parent) is List<object?> list)
        {
            Insert(parent, list.Count, value);
        }
        else
        {
            AddMember(parent, $"{Quoted((string)key)}: {Format(value, IndentAt(spans[Key(parent)].Start) + Indent)}");
        }
    }

    /// Inserts into a list at a position (the end when index is the list's size).
    public void Insert(List<object> listPath, int index, object? value)
    {
        List<object?> list = GetValue(listPath) as List<object?> ?? [];
        if (!Has(listPath))
        {
            SetValue(listPath, new List<object?> { value });
        }
        else if (index < list.Count)
        {
            Span before = spans[Key([.. listPath, index])];
            int lineStart = LineStart(before.MemberStart);
            bool onOwnLine = IsBlank(Sub(lineStart, before.MemberStart - lineStart));
            string indent = IndentAt(before.MemberStart);
            string item = Format(value, indent);
            if (onOwnLine)
            {
                Replace(lineStart, lineStart, indent + item + ",\n");
            }
            else
            {
                Replace(before.MemberStart, before.MemberStart, item + ", ");
            }
        }
        else
        {
            AddMember(listPath, Format(value, IndentAt(spans[Key(listPath)].Start) + Indent));
        }
    }

    public void Remove(List<object> path)
    {
        if (!Has(path) || path.Count == 0)
        {
            return;
        }
        Span span = spans[Key(path)];
        int from = span.MemberStart;
        int to = span.End;
        int after = SkipSpaces(to);
        bool commaAfter = after < Text.Length && Text[after] == ',';
        int before = PreviousMeaningful(from);
        int commaBefore = commaAfter || before < 0 || Text[before] != ',' ? -1 : before;
        if (commaAfter)
        {
            to = after + 1;
        }
        // A member alone on its lines takes its lines (and the note above it) with it; if it was the last one,
        // the comma that came before it goes too.
        int lineStart = LineStart(span.MemberStart);
        int lineEnd = LineEnd(to);
        string restOfLine = Sub(to, lineEnd - to).Trim();
        if (IsBlank(Sub(lineStart, span.MemberStart - lineStart)) && (restOfLine.Length == 0 || restOfLine.StartsWith("//")))
        {
            Replace(NoteStart(lineStart), Math.Min(lineEnd + 1, Text.Length), "");
            if (commaBefore >= 0)
            {
                Replace(commaBefore, commaBefore + 1, "");
            }
        }
        else
        {
            Replace(commaBefore >= 0 ? commaBefore : from, to, "");
        }
        CloseUpIfEmpty(path[..^1]);
    }

    /// An object or list left with nothing in it (not even a comment) is written {} or [].
    void CloseUpIfEmpty(List<object> containerPath)
    {
        Span span = spans[Key(containerPath)];
        string inside = Sub(span.Start + 1, span.End - span.Start - 2);
        if (inside.Length > 0 && IsBlank(inside))
        {
            Replace(span.Start + 1, span.End - 1, "");
        }
    }

    public void RenameKey(List<object> path, string newKey)
    {
        if (spans.TryGetValue(Key(path), out Span? span) && span.KeyStart >= 0 && !Has([.. path[..^1], newKey]))
        {
            Replace(span.KeyStart, span.KeyEnd, Quoted(newKey));
        }
    }

    /// The comment lines just above an entry, as plain text (one line each).
    public string Note(List<object> path)
    {
        if (!Has(path))
        {
            return "";
        }
        int lineStart = LineStart(spans[Key(path)].MemberStart);
        string comments = Sub(NoteStart(lineStart), lineStart - NoteStart(lineStart));
        IEnumerable<string> lines = comments.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => TrimPrefix(line.Trim(), "//").Trim());
        return string.Join("\n", lines);
    }

    public void SetNote(List<object> path, string noteText)
    {
        if (!Has(path))
        {
            return;
        }
        int lineStart = LineStart(spans[Key(path)].MemberStart);
        string indent = IndentAt(lineStart);
        IEnumerable<string> lines = noteText.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => indent + "// " + line.Trim() + "\n");
        Replace(NoteStart(lineStart), lineStart, string.Concat(lines));
    }

    /// A value written the way the game files are (VS Code's Format Document): one entry per line.
    public static string Format(object? value, string indent = "")
    {
        string inner = indent + Indent;
        switch (value)
        {
            case Dictionary<string, object?> { Count: > 0 } dictionary:
                return "{\n" + string.Join(",\n", dictionary.Select(pair => inner + Quoted(pair.Key) + ": " + Format(pair.Value, inner))) + "\n" + indent + "}";
            case List<object?> { Count: > 0 } list:
                return "[\n" + string.Join(",\n", list.Select(item => inner + Format(item, inner))) + "\n" + indent + "]";
            default:
                return OneLine(value);
        }
    }

    /// A value on one line, for a text box.
    public static string OneLine(object? value) => value switch
    {
        Dictionary<string, object?> { Count: 0 } => "{}",
        Dictionary<string, object?> dictionary => "{ " + string.Join(", ", dictionary.Select(pair => Quoted(pair.Key) + ": " + OneLine(pair.Value))) + " }",
        List<object?> list => "[" + string.Join(", ", list.Select(OneLine)) + "]",
        null => "null",
        bool flag => flag ? "true" : "false",
        string text => Quoted(text),
        double number when number == Math.Floor(number) => ((long)number).ToString(CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        long or int => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => Quoted(value.ToString() ?? ""),
    };

    /// Text in double quotes, as GDScript's JSON writes it (letters like ♠ stay as they are).
    public static string Quoted(string text)
    {
        StringBuilder quoted = new("\"");
        foreach (char character in text)
        {
            quoted.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)character:x4}",
                _ => character.ToString(),
            });
        }
        return quoted.Append('"').ToString();
    }

    void Replace(int from, int to, string newText)
    {
        SetText(Text[..from] + newText + Text[to..]);
    }

    /// Adds a member or item at the end of an object or list, matching its style (one line, or one entry per line);
    /// an empty one gets one entry per line.
    void AddMember(List<object> containerPath, string member)
    {
        Span span = spans[Key(containerPath)];
        int close = span.End - 1;
        object? container = GetValue(containerPath);
        List<string> children = container is Dictionary<string, object?> dictionary
            ? dictionary.Keys.Select(child => Key([.. containerPath, child])).ToList()
            : Enumerable.Range(0, ((List<object?>)container!).Count).Select(child => Key([.. containerPath, child])).ToList();
        if (children.Count == 0)
        {
            string emptyInside = Sub(span.Start + 1, close - span.Start - 1);
            string indent = IndentAt(span.Start);
            Replace(span.Start + 1, close, emptyInside.TrimEnd() + "\n" + indent + Indent + member.Trim() + "\n" + indent);
            return;
        }
        int lastEnd = children.Max(key => spans[key].End);
        bool hasComma = SkipSpaces(lastEnd) < Text.Length && Text[SkipSpaces(lastEnd)] == ',';
        int commaEnd = hasComma ? SkipSpaces(lastEnd) + 1 : lastEnd;
        int firstMember = children.Min(key => spans[key].MemberStart);
        // One member per line only when the first member starts its own line; a one-line object can hold a multi-line value.
        if (Sub(span.Start, firstMember - span.Start).Contains('\n'))
        {
            int lastMember = children.Max(key => spans[key].MemberStart);
            int lineEnd = LineEnd(commaEnd);
            Replace(lastEnd, lineEnd, "," + Sub(commaEnd, lineEnd - commaEnd) + "\n" + IndentAt(lastMember) + member.Trim());
        }
        else
        {
            Replace(commaEnd, commaEnd, (hasComma ? "" : ",") + " " + member.Trim());
        }
    }

    void IndexText()
    {
        spans.Clear();
        at = SkipSpaces(0);
        if (at < Text.Length)
        {
            Scan([], -1, -1, -1);
        }
    }

    void Scan(List<object> path, int memberStart, int keyStart, int keyEnd)
    {
        int start = at;
        char opening = Text[at];
        if (opening is '{' or '[')
        {
            at++;
            int index = 0;
            while (true)
            {
                at = SkipSpaces(at);
                if (at >= Text.Length || Text[at] is '}' or ']')
                {
                    break;
                }
                int itemStart = at;
                object key = index;
                int itemKeyEnd = -1;
                if (opening == '{')
                {
                    key = ScanString();
                    itemKeyEnd = at;
                    at = SkipSpaces(at) + 1;
                    at = SkipSpaces(at);
                }
                Scan([.. path, key], itemStart, opening == '{' ? itemStart : -1, itemKeyEnd);
                index++;
                at = SkipSpaces(at);
                if (at < Text.Length && Text[at] == ',')
                {
                    at++;
                }
            }
            at++;
        }
        else if (opening == '"')
        {
            ScanString();
        }
        else
        {
            while (at < Text.Length && !",]} \t\r\n/".Contains(Text[at]))
            {
                at++;
            }
        }
        spans[Key(path)] = new(start, at, memberStart >= 0 ? memberStart : start, keyStart, keyEnd);
    }

    string ScanString()
    {
        int start = at;
        at++;
        while (at < Text.Length && Text[at] != '"')
        {
            at += Text[at] == '\\' ? 2 : 1;
        }
        at++;
        return JsonSerializer.Deserialize<string>(Sub(start, at - start)) ?? "";
    }

    /// Past spaces, line breaks and comments.
    int SkipSpaces(int from)
    {
        int i = from;
        while (i < Text.Length)
        {
            if (Text[i] is ' ' or '\t' or '\r' or '\n')
            {
                i++;
            }
            else if (StartsAt(i, "//"))
            {
                i = LineEnd(i);
            }
            else if (StartsAt(i, "/*"))
            {
                int close = Text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close == -1 ? Text.Length : close + 2;
            }
            else
            {
                break;
            }
        }
        return i;
    }

    /// The last character before here that isn't a space, a line break or part of a comment (-1 if none).
    int PreviousMeaningful(int before)
    {
        int i = before - 1;
        while (i >= 0)
        {
            int comment = CommentStart(LineStart(i));
            if (comment != -1 && i >= comment)
            {
                i = comment - 1;
            }
            else if (Text[i] is ' ' or '\t' or '\r' or '\n')
            {
                i--;
            }
            else
            {
                return i;
            }
        }
        return -1;
    }

    /// Where a "//" comment starts on the line (outside quoted text), or -1.
    int CommentStart(int lineStart)
    {
        bool inText = false;
        int i = lineStart;
        while (i < Text.Length && Text[i] != '\n')
        {
            if (inText && Text[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (Text[i] == '"')
            {
                inText = !inText;
            }
            else if (!inText && StartsAt(i, "//"))
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    /// The start of the comment lines directly above a line (the line itself if there are none).
    int NoteStart(int lineStart)
    {
        int start = lineStart;
        while (start > 0)
        {
            int previous = LineStart(start - 1);
            if (!Sub(previous, start - previous).Trim().StartsWith("//"))
            {
                break;
            }
            start = previous;
        }
        return start;
    }

    int LineStart(int position) => position > 0 ? Text.LastIndexOf('\n', position - 1) + 1 : 0;

    int LineEnd(int position)
    {
        int end = Text.IndexOf('\n', Math.Min(position, Text.Length));
        return end == -1 ? Text.Length : end;
    }

    string IndentAt(int position)
    {
        int lineStart = LineStart(position);
        int end = lineStart;
        while (end < Text.Length && Text[end] is ' ' or '\t')
        {
            end++;
        }
        return Text[lineStart..end];
    }

    bool StartsAt(int position, string what) => string.CompareOrdinal(Text, position, what, 0, what.Length) == 0;

    /// GDScript's substr: as much of the text from there as there is.
    string Sub(int from, int length)
    {
        from = Math.Clamp(from, 0, Text.Length);
        return Text.Substring(from, Math.Clamp(length, 0, Text.Length - from));
    }

    static bool IsBlank(string text) => text.Trim().Length == 0;

    static string TrimPrefix(string text, string prefix) => text.StartsWith(prefix) ? text[prefix.Length..] : text;

    static string Key(List<object> path) => JsonSerializer.Serialize(path);
}
