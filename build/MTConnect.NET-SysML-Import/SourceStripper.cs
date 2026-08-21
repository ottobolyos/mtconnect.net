// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System.Text;

namespace MTConnect.NET_SysML_Import.Internal;

/// <summary>
/// Strips <c>//</c> line comments and <c>/* … */</c> block comments from a
/// C# source string while walking string and character literals verbatim so
/// a comment sigil embedded inside a literal is not chewed up. Every
/// literal form the C# language admits is handled explicitly: regular
/// (<c>"…"</c>), verbatim (<c>@"…"</c>), interpolated (<c>$"…"</c>),
/// interpolated-verbatim (<c>$@"…"</c> and <c>@$"…"</c>), and character
/// (<c>'…'</c>). Newlines are preserved verbatim so downstream regex
/// captures still report the original line numbers.
///
/// <para>
/// Consumed by the SysML importer's <see cref="Program"/>-scope
/// <c>ReadMTConnectVersionsMax</c> helper before applying the Max /
/// VersionXY regex pair, and by the
/// <c>MTConnect.NET_Generator_Tests.SourceStripperTests</c> fixture via a
/// shared-source link (the tests compile this file into their own assembly
/// rather than linking against the generator executable).
/// </para>
/// </summary>
internal static class SourceStripper
{
    /// <summary>
    /// Returns <paramref name="source"/> with every C# comment span
    /// replaced by same-width whitespace and every string/character
    /// literal preserved verbatim.
    /// </summary>
    public static string StripComments(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];

            // Line comment: `// ...` up to the next newline. Replace the
            // comment span with spaces so column positions in the trailing
            // line stay put. A line comment that runs to end-of-file
            // without a trailing newline terminates cleanly at the buffer
            // boundary — the while loop exits on `i < source.Length`.
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    sb.Append(' ');
                    i++;
                }
                continue;
            }

            // Block comment: `/* ... */`. Replace the entire span with
            // spaces (and preserve embedded newlines verbatim so line
            // numbers survive). An unterminated block comment consumes
            // the rest of the file — matches Roslyn's error-recovery
            // behaviour rather than raising here.
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                sb.Append(' ');
                sb.Append(' ');
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    {
                        sb.Append(' ');
                        sb.Append(' ');
                        i += 2;
                        break;
                    }
                    sb.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                continue;
            }

            // Interpolated-verbatim string literal in the `@$"…"` order.
            // C# treats `@$"…"` as equivalent to `$@"…"`. The fallback of
            // "append the bare `@`, then walk `$"…"` as regular
            // interpolated" produces byte-identical output for the
            // present use case (string content is preserved verbatim in
            // BOTH branches, and the buggy walker's premature termination
            // at each `""` is immediately re-entered as a new regular
            // string, so no `//` ever leaks into top-level state between
            // atoms). This explicit branch is a code-clarity move that
            // matches the class-level docstring's claim that every C# 8+
            // interpolated-verbatim ordering is recognised; the CURRENT
            // observable stripper behaviour is unchanged. Checked BEFORE
            // the `@"…"` branch so the two-sigil form takes precedence.
            if (c == '@' && i + 2 < source.Length && source[i + 1] == '$' && source[i + 2] == '"')
            {
                sb.Append(source, i, 3);
                i += 3;
                WalkVerbatim(source, sb, ref i);
                continue;
            }

            // Verbatim string literal: `@"…"`. A doubled quote (`""`) is
            // an escaped quote inside the literal; the terminator is a
            // single unescaped quote.
            if (c == '@' && i + 1 < source.Length && source[i + 1] == '"')
            {
                sb.Append(source, i, 2);
                i += 2;
                WalkVerbatim(source, sb, ref i);
                continue;
            }

            // Interpolated-verbatim in the `$@"…"` order.
            if (c == '$' && i + 2 < source.Length && source[i + 1] == '@' && source[i + 2] == '"')
            {
                sb.Append(source, i, 3);
                i += 3;
                WalkVerbatim(source, sb, ref i);
                continue;
            }

            // Regular / interpolated string literal: `"…"` or `$"…"`. A
            // backslash escapes the next character (including `\"` and
            // `\\`).
            if (c == '"' || (c == '$' && i + 1 < source.Length && source[i + 1] == '"'))
            {
                if (c == '$')
                {
                    sb.Append('$');
                    sb.Append('"');
                    i += 2;
                }
                else
                {
                    sb.Append('"');
                    i++;
                }
                WalkRegular(source, sb, ref i);
                continue;
            }

            // Character literal: `'x'` or `'\x'`. Same escape rule as
            // regular strings.
            if (c == '\'')
            {
                sb.Append('\'');
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        sb.Append(source[i]);
                        sb.Append(source[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (source[i] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        break;
                    }
                    sb.Append(source[i]);
                    i++;
                }
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    // Walks a verbatim string literal (`@"…"`, `$@"…"`, or `@$"…"`)
    // starting AFTER the opening sigil / quote has been appended, until
    // the closing single unescaped quote is consumed. `""` is an escaped
    // quote inside the literal.
    private static void WalkVerbatim(string source, StringBuilder sb, ref int i)
    {
        while (i < source.Length)
        {
            if (source[i] == '"')
            {
                if (i + 1 < source.Length && source[i + 1] == '"')
                {
                    sb.Append('"');
                    sb.Append('"');
                    i += 2;
                    continue;
                }
                sb.Append('"');
                i++;
                return;
            }
            sb.Append(source[i]);
            i++;
        }
    }

    // Walks a regular / interpolated (non-verbatim) string literal
    // starting AFTER the opening quote has been appended, until the
    // closing unescaped quote is consumed. `\` escapes the next char.
    private static void WalkRegular(string source, StringBuilder sb, ref int i)
    {
        while (i < source.Length)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                sb.Append(source[i]);
                sb.Append(source[i + 1]);
                i += 2;
                continue;
            }
            if (source[i] == '"')
            {
                sb.Append('"');
                i++;
                return;
            }
            sb.Append(source[i]);
            i++;
        }
    }
}
