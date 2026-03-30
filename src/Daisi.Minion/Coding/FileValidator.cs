namespace Daisi.Minion.Coding;

/// <summary>
/// Validates file structure (not style or content) for common file types.
/// Returns specific errors the model can fix.
/// </summary>
public static class FileValidator
{
    /// <summary>
    /// Validate a file's structural integrity based on its extension.
    /// Returns null if valid, or a list of structural errors.
    /// </summary>
    public static List<string>? Validate(string path, string content)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var errors = ext switch
        {
            ".html" or ".htm" => ValidateHtml(content),
            ".css" => ValidateCss(content),
            ".js" => ValidateJs(content),
            ".json" => ValidateJson(content),
            ".xml" or ".svg" or ".xaml" => ValidateXml(content),
            ".cs" => ValidateCSharp(content),
            _ => null,
        };

        return errors is { Count: 0 } ? null : errors;
    }

    private static int LineAt(string content, int charPos)
    {
        int line = 1;
        for (int j = 0; j < charPos && j < content.Length; j++)
            if (content[j] == '\n') line++;
        return line;
    }

    private static List<string> ValidateHtml(string content)
    {
        var errors = new List<string>();

        // Check tag balance — track (tagName, line) so errors report where the tag opened
        var tagStack = new Stack<(string Name, int Line)>();
        var selfClosing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "br", "hr", "img", "input", "meta", "link", "area", "base",
            "col", "embed", "param", "source", "track", "wbr"
        };

        int i = 0;
        while (i < content.Length)
        {
            var tagStart = content.IndexOf('<', i);
            if (tagStart < 0) break;

            var tagEnd = content.IndexOf('>', tagStart);
            if (tagEnd < 0)
            {
                errors.Add($"Line {LineAt(content, tagStart)}: Unclosed < bracket");
                break;
            }

            var tag = content[(tagStart + 1)..tagEnd].Trim();
            var line = LineAt(content, tagStart);
            i = tagEnd + 1;

            // Skip comments, doctype, processing instructions
            if (tag.StartsWith('!') || tag.StartsWith('?')) continue;

            // Self-closing tag like <br/>
            if (tag.EndsWith('/'))
                continue;

            // Closing tag
            if (tag.StartsWith('/'))
            {
                var closeName = tag[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (tagStack.Count == 0)
                    errors.Add($"Line {line}: Unexpected closing tag </{closeName}> with no matching open tag");
                else if (!tagStack.Peek().Name.Equals(closeName, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Line {line}: Mismatched tags: expected </{tagStack.Peek().Name}> (opened line {tagStack.Peek().Line}), found </{closeName}>");
                else
                    tagStack.Pop();
                continue;
            }

            // Opening tag
            var tagName = tag.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (selfClosing.Contains(tagName)) continue;

            tagStack.Push((tagName, line));
        }

        foreach (var (name, line) in tagStack)
            errors.Add($"Line {line}: Unclosed tag: <{name}>");

        // Basic structure checks
        if (!content.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) &&
            !content.Contains("<html", StringComparison.OrdinalIgnoreCase))
            errors.Add("Missing <!DOCTYPE html> or <html> tag");

        return errors;
    }

    private static List<string> ValidateCss(string content)
    {
        var errors = new List<string>();

        // Brace balance
        int braces = 0, line = 1;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n') line++;
            if (content[i] == '{') braces++;
            else if (content[i] == '}') braces--;
            if (braces < 0) { errors.Add($"Line {line}: Extra closing brace"); break; }
        }
        if (braces > 0) errors.Add($"{braces} unclosed brace(s) — check that every {{ has a matching }}");

        // Unclosed comments
        var commentStart = 0;
        while ((commentStart = content.IndexOf("/*", commentStart, StringComparison.Ordinal)) >= 0)
        {
            var commentEnd = content.IndexOf("*/", commentStart + 2, StringComparison.Ordinal);
            if (commentEnd < 0) { errors.Add("Unclosed CSS comment /*"); break; }
            commentStart = commentEnd + 2;
        }

        return errors;
    }

    private static List<string> ValidateJs(string content)
    {
        var errors = new List<string>();

        // Brace balance (rough — doesn't account for strings/comments)
        int braces = 0, parens = 0, brackets = 0;
        bool inString = false;
        char stringChar = '"';

        for (int i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inString)
            {
                if (c == stringChar && (i == 0 || content[i - 1] != '\\'))
                    inString = false;
                continue;
            }

            if (c is '"' or '\'' or '`') { inString = true; stringChar = c; continue; }
            if (c == '/' && i + 1 < content.Length && content[i + 1] == '/') // line comment
            {
                i = content.IndexOf('\n', i);
                if (i < 0) break;
                continue;
            }

            if (c == '{') braces++;
            else if (c == '}') braces--;
            else if (c == '(') parens++;
            else if (c == ')') parens--;
            else if (c == '[') brackets++;
            else if (c == ']') brackets--;
        }

        if (braces != 0) errors.Add($"Unbalanced braces: {braces} extra {(braces > 0 ? "opening" : "closing")}");
        if (parens != 0) errors.Add($"Unbalanced parentheses: {parens} extra {(parens > 0 ? "opening" : "closing")}");
        if (brackets != 0) errors.Add($"Unbalanced brackets: {brackets} extra {(brackets > 0 ? "opening" : "closing")}");

        return errors;
    }

    private static List<string> ValidateJson(string content)
    {
        var errors = new List<string>();
        try
        {
            System.Text.Json.JsonDocument.Parse(content);
        }
        catch (System.Text.Json.JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
        }
        return errors;
    }

    private static List<string> ValidateXml(string content)
    {
        var errors = new List<string>();
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(content);
        }
        catch (System.Xml.XmlException ex)
        {
            errors.Add($"Invalid XML: {ex.Message}");
        }
        return errors;
    }

    private static List<string> ValidateCSharp(string content)
    {
        var errors = new List<string>();

        int braces = 0;
        bool inString = false, inVerbatim = false, inLineComment = false, inBlockComment = false;

        for (int i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inLineComment) { if (c == '\n') inLineComment = false; continue; }
            if (inBlockComment)
            {
                if (c == '*' && i + 1 < content.Length && content[i + 1] == '/')
                { inBlockComment = false; i++; }
                continue;
            }
            if (inString)
            {
                if (inVerbatim) { if (c == '"' && (i + 1 >= content.Length || content[i + 1] != '"')) { inString = false; inVerbatim = false; } else if (c == '"') i++; }
                else { if (c == '"' && content[i - 1] != '\\') inString = false; }
                continue;
            }

            if (c == '/' && i + 1 < content.Length)
            {
                if (content[i + 1] == '/') { inLineComment = true; continue; }
                if (content[i + 1] == '*') { inBlockComment = true; i++; continue; }
            }
            if (c == '@' && i + 1 < content.Length && content[i + 1] == '"') { inString = true; inVerbatim = true; i++; continue; }
            if (c == '"') { inString = true; continue; }

            if (c == '{') braces++;
            else if (c == '}') braces--;
        }

        if (braces != 0) errors.Add($"Unbalanced braces: {braces} extra {(braces > 0 ? "opening" : "closing")}");
        if (inBlockComment) errors.Add("Unclosed block comment /*");

        return errors;
    }
}
