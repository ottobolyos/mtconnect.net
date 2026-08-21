// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using MTConnect.NET_SysML_Import.Internal;
using NUnit.Framework;

namespace MTConnect.NET_Generator_Tests
{
    /// <summary>
    /// Direct unit-test coverage for <see cref="SourceStripper.StripComments"/>.
    ///
    /// <para>
    /// The helper is consumed by the SysML importer's
    /// <c>ReadMTConnectVersionsMax</c> parser before applying the Max /
    /// VersionXY regex pair; without correct comment-stripping a stale
    /// <c>// Max =&gt; Version27;</c> line commented out during a version
    /// bump would win the first-match against the live declaration below
    /// it, pinning PREV_VERSION to the wrong version. The
    /// <c>AutoDerivePreviousXmiTests.Commented_out_Max_declaration_does_not_confuse_the_parser</c>
    /// integration test pins that end-to-end behaviour on one composite
    /// input; this fixture pins each individual literal-handling branch in
    /// isolation so a regression in any single arm (verbatim, interpolated,
    /// char-literal, EOF-line-comment, etc.) surfaces immediately with a
    /// branch-scoped failure message rather than a downstream regex miss.
    /// </para>
    ///
    /// <para>
    /// The stripper is exercised via a shared-source link on
    /// <c>SourceStripper.cs</c> in the test csproj — the generator project's
    /// <c>bin/</c> is not linked (the byte-identical-regen tests still
    /// treat the generator as an external CLI). See the csproj comment
    /// block on the <c>&lt;Compile Include&gt;</c> element for the
    /// architecture rationale.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SourceStripperTests
    {
        // ---- comment-stripping (positive) ------------------------------

        /// <summary>
        /// A `// …` line comment is replaced by same-width whitespace up
        /// to the next newline; the trailing newline is preserved so
        /// downstream line-number reporting stays anchored.
        /// </summary>
        [Test]
        public void Line_comment_is_replaced_with_spaces_and_preserves_newline()
        {
            var input = "int x = 1; // decoy\nint y = 2;\n";
            var output = SourceStripper.StripComments(input);
            // Rebuild the expected shape programmatically so the space run is
            // pinned by the invariant ("comment span width matches"), not by a
            // hand-counted literal that's easy to get off-by-one on.
            var expected = "int x = 1; " + new string(' ', "// decoy".Length) + "\nint y = 2;\n";
            Assert.That(output, Is.EqualTo(expected),
                "Line comment must be replaced by a run of spaces the same width as the "
                + "comment span (including the leading `//`), with the trailing newline "
                + "preserved verbatim so column and line positions stay put.");
        }

        /// <summary>
        /// A line comment that runs to end-of-file WITHOUT a trailing
        /// newline is a boundary case: the walker must terminate cleanly
        /// on `i &lt; source.Length` rather than dereference past the end.
        /// </summary>
        [Test]
        public void Line_comment_at_end_of_file_without_trailing_newline_terminates_cleanly()
        {
            var comment = "// trailing decoy no newline";
            var input = "int x = 1; " + comment;
            var output = SourceStripper.StripComments(input);
            var expected = "int x = 1; " + new string(' ', comment.Length);
            Assert.That(output, Is.EqualTo(expected),
                "A line comment at EOF without a trailing newline must strip to spaces "
                + "and terminate cleanly, not throw IndexOutOfRangeException or drop "
                + "the last character.");
            Assert.That(output.Length, Is.EqualTo(input.Length),
                "Length invariant must hold even at the EOF boundary.");
        }

        /// <summary>
        /// A `/* … */` block comment is replaced by whitespace with
        /// embedded newlines preserved so downstream regexes still report
        /// original line numbers.
        /// </summary>
        [Test]
        public void Block_comment_replaces_span_with_whitespace_preserving_embedded_newlines()
        {
            // Block-comment span: `/* line-a\n   line-b */` — two physical
            // lines. The stripper replaces every non-newline character
            // with a space so column widths are preserved on each line
            // individually and the embedded newline survives.
            var input = "int x = 1;\n/* line-a\n   line-b */\nint y = 2;\n";
            var output = SourceStripper.StripComments(input);
            var expected = "int x = 1;\n"
                + new string(' ', "/* line-a".Length)
                + "\n"
                + new string(' ', "   line-b */".Length)
                + "\nint y = 2;\n";
            Assert.That(output, Is.EqualTo(expected),
                "Block comment must map to same-width whitespace on each line and "
                + "preserve embedded newlines verbatim so line numbers in downstream "
                + "regex captures still point at the original source.");
            Assert.That(output.Length, Is.EqualTo(input.Length),
                "Length invariant must hold across a multi-line block comment.");
        }

        /// <summary>
        /// Regression pin: a block comment that spans many lines must NOT
        /// collapse them all onto one line (a common bug shape in a
        /// hand-rolled stripper).
        /// </summary>
        [Test]
        public void Block_comment_spanning_multiple_lines_preserves_line_count()
        {
            var input = "A\n/* one\ntwo\nthree */ B\n";
            var output = SourceStripper.StripComments(input);
            var newlineCount = 0;
            foreach (var c in output) if (c == '\n') newlineCount++;
            Assert.That(newlineCount, Is.EqualTo(4),
                "Every embedded newline inside the block comment must survive so a "
                + "downstream `Regex.Match` on a stripped file still reports true line "
                + "numbers.");
        }

        // ---- literal-handling (verbatim walks) -------------------------

        /// <summary>
        /// A regular string literal containing a `//` sigil must be
        /// preserved verbatim — the string handler runs BEFORE the
        /// line-comment handler would fire on any `/` character inside
        /// the literal.
        /// </summary>
        [Test]
        public void Regular_string_literal_with_line_comment_sigil_is_preserved()
        {
            var input = "var s = \"// not a comment\";";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo(input),
                "Regular string content must survive the strip — the `//` inside the "
                + "literal is data, not a comment.");
        }

        /// <summary>
        /// A regular string literal containing a `/* */` sigil must be
        /// preserved verbatim.
        /// </summary>
        [Test]
        public void Regular_string_literal_with_block_comment_sigil_is_preserved()
        {
            var input = "var s = \"/* not a comment */\";";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo(input),
                "Regular string content must survive the strip.");
        }

        /// <summary>
        /// A regular string with an escaped quote (`\\\"`) must terminate
        /// at the CORRECT closing quote — the backslash-escape handler
        /// consumes the `\\\"` as two chars, not one.
        /// </summary>
        [Test]
        public void Regular_string_literal_with_escaped_quote_terminates_correctly()
        {
            // Source: var s = "a\"b"; more // comment
            var input = "var s = \"a\\\"b\"; more // comment";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo("var s = \"a\\\"b\"; more           "),
                "The `\\\"` inside the string must NOT terminate it — the walker's "
                + "backslash-escape branch keeps the string boundaries intact so the "
                + "trailing `// comment` still strips.");
        }

        /// <summary>
        /// An interpolated string literal (`$"…"`) must be walked the
        /// same as a regular string — `\\` escapes, `"` terminates.
        /// </summary>
        [Test]
        public void Interpolated_string_literal_dollar_form_is_preserved()
        {
            var input = "var s = $\"hello // world\";";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo(input),
                "Interpolated string content must survive verbatim.");
        }

        /// <summary>
        /// A verbatim string literal (`@"…"`) uses `""` as an escaped
        /// quote (not `\\"`). The walker must consume `""` as two chars
        /// rather than treating the first `"` as a terminator.
        /// </summary>
        [Test]
        public void Verbatim_string_literal_at_form_preserves_doubled_quote_escape()
        {
            var input = "var s = @\"a\"\"b // decoy\";";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo(input),
                "Verbatim string with a `\"\"` escape must NOT terminate at the first "
                + "`\"` — the walker's doubled-quote branch preserves the escape and "
                + "the trailing `// decoy` stays inside the literal.");
        }

        /// <summary>
        /// A verbatim string can contain literal newlines (unlike
        /// regular strings). The walker must copy them through without
        /// terminating the literal.
        /// </summary>
        [Test]
        public void Verbatim_string_literal_preserves_embedded_newlines()
        {
            var input = "var s = @\"line1\nline2 // still literal\";";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo(input),
                "Verbatim string content, including embedded newlines and the `//` "
                + "on the second line, must survive verbatim.");
        }

        /// <summary>
        /// The interpolated-verbatim `$@"…"` ordering must be recognised
        /// AS a verbatim literal — the walker uses `""` for the escape,
        /// not `\\"`.
        /// </summary>
        [Test]
        public void Interpolated_verbatim_string_dollar_at_form_preserves_doubled_quote_escape()
        {
            var input = "var s = $@\"a\"\"b // decoy\";";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo(input),
                "$@\"…\" must be walked with the verbatim escape rule (`\"\"`), not the "
                + "backslash rule — otherwise the first `\"` terminates the string and "
                + "the following `// decoy` gets chewed as a comment.");
        }

        /// <summary>
        /// The alternate interpolated-verbatim `@$"…"` ordering (legal
        /// since C# 8.0, equivalent to `$@"…"`) must also be recognised
        /// as a verbatim literal. A previous version of the stripper
        /// only handled `$@"…"` and fell through on `@$"…"` — the bare
        /// `@` got appended and the following `$"…"` was walked with the
        /// regular-string backslash-escape rule, corrupting any `""`
        /// escape inside.
        /// </summary>
        [Test]
        public void Interpolated_verbatim_string_at_dollar_form_preserves_doubled_quote_escape()
        {
            var input = "var s = @$\"a\"\"b // decoy\";";
            var output = SourceStripper.StripComments(input);
            Assert.That(output, Is.EqualTo(input),
                "@$\"…\" is C#'s alternate ordering of $@\"…\" (both equivalent since "
                + "C# 8.0). The walker must apply the verbatim escape rule (`\"\"`) — "
                + "not fall through to the regular-string branch, which would truncate "
                + "at the first `\"` and misclassify the trailing `// decoy` as a comment.");
        }

        // ---- character literal branches --------------------------------

        /// <summary>
        /// A character literal must be preserved verbatim; a `/` inside
        /// a character literal must NOT trip the line-comment sniff.
        /// </summary>
        [Test]
        public void Character_literal_containing_slash_is_preserved()
        {
            var comment = "// trailing decoy";
            var input = "char c = '/'; " + comment;
            var output = SourceStripper.StripComments(input);
            var expected = "char c = '/'; " + new string(' ', comment.Length);
            Assert.That(output, Is.EqualTo(expected),
                "The `/` inside the character literal must not trigger the line-comment "
                + "handler; the trailing `// trailing decoy` must strip normally.");
        }

        /// <summary>
        /// A character literal with an escape sequence (`'\\''`, `'\\n'`,
        /// `'\\\\'`) must consume the backslash + next char as one atom
        /// so the closing quote is found correctly.
        /// </summary>
        [Test]
        public void Character_literal_with_escape_sequence_is_preserved()
        {
            // char q = '\''; // decoy   — the `\'` escape must not terminate
            // the literal at the first `'`.
            var comment = "// decoy";
            var input = "char q = '\\''; " + comment;
            var output = SourceStripper.StripComments(input);
            var expected = "char q = '\\''; " + new string(' ', comment.Length);
            Assert.That(output, Is.EqualTo(expected),
                "A `\\'` inside a character literal must NOT terminate the literal — "
                + "the escape rule mirrors regular strings; the trailing `// decoy` "
                + "strips normally.");
        }

        // ---- integration-style pin (composite) -------------------------

        /// <summary>
        /// End-to-end pin: a source string carrying every literal
        /// shape (regular, `@`-verbatim, `$`-interp, `$@`-interp-verbatim,
        /// `@$`-interp-verbatim, char) plus `//` and `/* */` comments,
        /// mixed together — every literal must survive verbatim and
        /// every comment must strip to whitespace.
        /// </summary>
        [Test]
        public void Composite_input_strips_only_comments_not_literals()
        {
            var input =
                "var a = \"// r\";\n"
                + "var b = @\"// v\";\n"
                + "var c = $\"// i\";\n"
                + "var d = $@\"// iv1\";\n"
                + "var e = @$\"// iv2\";\n"
                + "char f = '/'; // trailing\n"
                + "/* block */\n"
                + "var g = 42;\n";

            var output = SourceStripper.StripComments(input);

            Assert.That(output, Does.Contain("\"// r\""), "regular string content preserved");
            Assert.That(output, Does.Contain("@\"// v\""), "verbatim string content preserved");
            Assert.That(output, Does.Contain("$\"// i\""), "interpolated string content preserved");
            Assert.That(output, Does.Contain("$@\"// iv1\""), "$@ verbatim content preserved");
            Assert.That(output, Does.Contain("@$\"// iv2\""), "@$ verbatim content preserved");
            Assert.That(output, Does.Contain("'/'"), "character literal content preserved");
            Assert.That(output, Does.Not.Contain("// trailing"), "trailing line comment stripped");
            Assert.That(output, Does.Not.Contain("/* block */"), "block comment stripped");
            Assert.That(output, Does.Contain("var g = 42;"), "post-comment code intact");
        }

        // ---- line-number preservation invariant ------------------------

        /// <summary>
        /// The stripped output must have EXACTLY the same character
        /// length as the input, and the same number of newlines. This
        /// is the load-bearing invariant for downstream regex line
        /// numbers.
        /// </summary>
        [Test]
        public void Strip_preserves_length_and_newline_count()
        {
            var input =
                "// a comment\n"
                + "int x = 1; // trailing\n"
                + "/* block\n   spanning */\n"
                + "var s = @\"literal\n content\";\n"
                + "char c = '\\n';\n";

            var output = SourceStripper.StripComments(input);
            int inNewlines = 0, outNewlines = 0;
            foreach (var c in input) if (c == '\n') inNewlines++;
            foreach (var c in output) if (c == '\n') outNewlines++;

            Assert.That(output.Length, Is.EqualTo(input.Length),
                "Output length must equal input length so column positions in downstream "
                + "regex captures still point at the original source.");
            Assert.That(outNewlines, Is.EqualTo(inNewlines),
                "Newline count must be preserved so line numbers in downstream regex "
                + "captures still report the original position.");
        }
    }
}
