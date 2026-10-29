using System.Security.Cryptography;
using System.Text;

namespace Atria.Script;

public sealed class ScriptException : Exception
{
    public string Code
    {
        get;
    }
    public string Member
    {
        get;
    }
    public int ByteOffset
    {
        get;
    }

    public ScriptException(string code, string member, int byteOffset, string detail) : base($"{member}+{byteOffset}: {code}: {detail}") => (Code, Member, ByteOffset) = (code, member, byteOffset);
}

public enum ScriptTokenKind
{
    End,
    Identifier,
    Number,
    String,
    Symbol
}

public readonly record struct ScriptToken(ScriptTokenKind Kind, string Text, int Offset)
{
    // Text is a reversible Latin-1 byte container, never decoded dialogue text.
    public byte[] RawBytes => Encoding.Latin1.GetBytes(Text);
}

public sealed record ScriptFunction(string Name, string[] Parameters, int BodyToken, int EndToken, int ByteOffset);
/// <summary>Immutable byte-oriented scenario index. Provenance: 0x43b350, 0x43bec0.</summary>
public sealed class ScriptProgram
{
    public string MemberName
    {
        get;
    }
    public string SourceSha256
    {
        get;
    }
    public bool LibraryRepairApplied
    {
        get;
    }
    public IReadOnlyList<string> AppliedRepairs
    {
        get;
    }
    public IReadOnlyList<ScriptToken> Tokens
    {
        get;
    }
    public IReadOnlyDictionary<string, ScriptFunction> Functions
    {
        get;
    }
    public IReadOnlyList<string> GlobalVariables
    {
        get;
    }
    internal Dictionary<int, int> BlockEnds
    {
        get;
    }

    private ScriptProgram(
        string name,
        string hash,
        bool repair,
        List<string> repairs,
        ScriptToken[] tokens,
        Dictionary<string, ScriptFunction> functions,
        List<string> globals,
        Dictionary<int, int> blocks
    ) => (MemberName, SourceSha256, LibraryRepairApplied, AppliedRepairs, Tokens, Functions, GlobalVariables, BlockEnds) = (name, hash, repair, repairs.AsReadOnly(), Array.AsReadOnly(tokens), functions, globals.AsReadOnly(), blocks);
    public static ScriptProgram Parse(string memberName, ReadOnlySpan<byte> bytes, bool applyKnownRepairs = true)
    {
        string repairMember = Path.GetFileNameWithoutExtension(memberName);
        byte[] owned = bytes.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(owned)).ToLowerInvariant();
        bool repaired = false;
        var repairs = new List<string>();
        if (applyKnownRepairs && repairMember.Equals("02_EVE1", StringComparison.OrdinalIgnoreCase) && owned.Length == 122
&& hash == "8630a2cf8fc0072daa44c7ea0d9f32f34e8434a9b0df2199a5821f9ca22ae1dc")
        {
            // Exact shipped Gatha guild lifecycle omits script_end in all four handlers.
            // Inferred source repair: stop repeated queued enter; retain permission and quest data.
            owned = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(owned).Replace("}", "    script_end();\r\n}", StringComparison.Ordinal));
            repairs.Add("GATHA-GUILD-002-LIFECYCLE-END");
        }

        if (applyKnownRepairs && repairMember.Equals("32_LIBRA", StringComparison.OrdinalIgnoreCase) && owned.Length == 2063
&& hash == "ed79301ba9bcdf6f8e69958436836400dac72ef1a4a26476b02ee33b61c3508a")
        {
            ";\r\n}\r\n"u8.CopyTo(owned.AsSpan(2057, 6));
            repaired = true;
            repairs.Add("LIBRARY-001");
        }

        if (applyKnownRepairs && repairMember.Equals("SUBEVENT", StringComparison.OrdinalIgnoreCase) && owned.Length == 523350
&& hash == "b6b93bbc65c1788447ac4cf996ea56518df99c0de0f5d84a76e2f66ee57e3723")
        {
            // 43c060 requires ';' after an identifier expression. Replace only the
            // first CRLF of a blank line with '; ', retaining length/source offsets.
            // The remaining CRLF still terminates the line; no story content changes.
            "; "u8.CopyTo(owned.AsSpan(285054, 2));
            "; "u8.CopyTo(owned.AsSpan(509268, 2));
            repairs.Add("SUBEVENT-074-TERMINATOR");
            repairs.Add("SUBEVENT-108-TERMINATOR");
            owned = Quest63RecoveredSource.Append(owned);
            repairs.Add("QUEST-063-PATCH-161-162-RU-TRANSLATION");
        }

        if (applyKnownRepairs && repairMember.Equals("35_BAR", StringComparison.OrdinalIgnoreCase) && owned.Length == 7842
&& hash == "73f64dce39e1d4fd53762dc85cc59b075a5870f1f0407cf6b9842492e424b501")
        {
            // 43c0aa/43c6d0 seed the malformed leading && with3. The extra ')'
            // makes a false inner condition skip BOTH blocks (43b250), including
            // the outer return. Combine guards to preserve that fallthrough.
            // See docs/BUG_35_BAR_EVALUATOR.md; this is not a guessed quest gate.
            ReadOnlySpan<byte> original = "    if ( (b == 1) && (c == 1) && (d == 1) ) {\r\n       if ( && (e == 1) && (f == 1) && (g == 0) ) ) {\r\n"u8;
            Span<byte> header = owned.AsSpan(3354, original.Length);
            Span<byte> innerClose = owned.AsSpan(3850, 9);
            if (!header.SequenceEqual(original) || !innerClose.SequenceEqual("        }"u8))
            {
                throw new InvalidDataException("35_BAR normalization source interval mismatch.");
            }

            header.Fill(32);
            "    if ( (b == 1) && (c == 1) && (d == 1) && (e == 1) && (f == 1) && (g == 0) ) {\r\n"u8.CopyTo(header);
            innerClose.Fill(32);
            repairs.Add("VIDON-035-NATIVE-CONDITION-NORMALIZATION");
        }

        if (applyKnownRepairs && repairMember.Equals("5012SHRI", StringComparison.OrdinalIgnoreCase) && owned.Length == 10490
&& hash == "2e28879db63e19468eb889840490db8016d991731d8c157dbd0e595b35cf98df")
        {
            // Inferred intentional repair, not recovered corrected original behavior.
            // The parallel ending and both catalogs identify existing narration278.
            // Preserve supplied Russian text and every source offset; see BUG_TEMPLE_DIALOGUE.md.
            ReadOnlySpan<byte> oldKey = Convert.FromHexString("533531415fb6f3c8c4323738");
            if (!owned.AsSpan(8626, 27).SequenceEqual("dialogue_script2( -1, -1, \""u8) || !owned.AsSpan(8653, 12).SequenceEqual(oldKey)
|| !owned.AsSpan(8665, 4).SequenceEqual("\" );"u8))
            {
                throw new InvalidDataException("5012SHRI narration repair source interval mismatch.");
            }

            Convert.FromHexString("533531415fbcb3b8ed323738").CopyTo(owned.AsSpan(8653, 12));
            repairs.Add("TEMPLE-5012-NARRATION-278");
        }

        ScriptToken[] tokens = Tokenize(memberName, owned).ToArray();
        var blocks = new Dictionary<int, int>();
        var openings = new Stack<int>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Kind != ScriptTokenKind.Symbol)
            {
                continue;
            }

            if (tokens[i].Text == "{")
            {
                openings.Push(i);
            }
            else if (tokens[i].Text == "}")
            {
                if (openings.Count == 0)
                {
                    throw Error("UNMATCHED_BRACE", i, "Unexpected closing brace.");
                }

                blocks.Add(openings.Pop(), i);
            }
        }

        if (openings.Count != 0)
        {
            throw Error("UNCLOSED_BLOCK", openings.Peek(), "End of member before closing brace.");
        }

        var functions = new Dictionary<string, ScriptFunction>(StringComparer.Ordinal);
        var globals = new List<string>();
        int cursor = 0;
        while (tokens[cursor].Kind != ScriptTokenKind.End)
        {
            if (tokens[cursor].Text is "int" or "char")
            {
                cursor++;
                do
                {
                    globals.Add(Identifier());
                }
                while (Take(","));
                Expect(";");
                continue;
            }

            int start = cursor;
            string name = Identifier();
            Expect("(");
            var parameters = new List<string>();
            if (!Take(")"))
            {
                do
                {
                    if (tokens[cursor].Text is not ("int" or "char"))
                    {
                        throw Error("PARAMETER_TYPE", cursor, "Expected int or char parameter type.");
                    }

                    cursor++;
                    parameters.Add(Identifier());
                }
                while (Take(","));
                Expect(")");
            }

            int body = cursor;
            Expect("{");
            int end = blocks[body];
            // Original lookup 0x43b0c0 uses first match, including duplicate definitions.
            functions.TryAdd(name, new(name, parameters.ToArray(), body, end, tokens[start].Offset));
            cursor = end + 1;
        }

        return new(memberName, hash, repaired, repairs, tokens, functions, globals, blocks);
        ScriptException Error(string code, int at, string detail) => new(code, memberName, tokens[Math.Min(at, tokens.Length - 1)].Offset, detail);
        string Identifier()
        {
            if (tokens[cursor].Kind != ScriptTokenKind.Identifier)
            {
                throw Error("EXPECTED_IDENTIFIER", cursor, "Expected ASCII identifier.");
            }

            return tokens[cursor++].Text;
        }

        bool Take(string s)
        {
            if (tokens[cursor].Text != s)
            {
                return false;
            }

            cursor++;
            return true;
        }

        void Expect(string s)
        {
            if (!Take(s))
            {
                throw Error("EXPECTED_TOKEN", cursor, $"Expected '{s}'.");
            }
        }
    }

    private static List<ScriptToken> Tokenize(string member, byte[] bytes)
    {
        var result = new List<ScriptToken>();
        int p = 0;
        while (p < bytes.Length && bytes[p] != 0)
        {
            byte b = bytes[p];
            if (b is 9 or 10 or 13 or 32)
            {
                p++;
                continue;
            }

            int start = p;
            if (b == '/' && p + 1 < bytes.Length && bytes[p + 1] == '/')
            {
                p += 2;
                while (p < bytes.Length && bytes[p] is not (0 or 10 or 13))
                {
                    p++;
                }

                continue;
            }

            if (b == '/' && p + 1 < bytes.Length && bytes[p + 1] == '*')
            {
                p += 2;
                while (p + 1 < bytes.Length && bytes[p] != 0 && !(bytes[p] == '*' && bytes[p + 1] == '/'))
                {
                    p++;
                }

                if (p + 1 >= bytes.Length || bytes[p] == 0)
                {
                    throw Error("UNCLOSED_COMMENT", start);
                }

                p += 2;
                continue;
            }

            if (b == '"')
            {
                p++;
                int content = p;
                while (p < bytes.Length && bytes[p] is not (0 or 13 or 10 or 34))
                {
                    p++;
                }

                if (p >= bytes.Length || bytes[p] != '"')
                {
                    throw Error("UNCLOSED_STRING", start);
                }
                // Native tokenizer converts tabs to spaces; it has no backslash escape syntax.
                result.Add(new(ScriptTokenKind.String, Encoding.Latin1.GetString(bytes, content, p - content).Replace('\t', ' '), start));
                p++;
                continue;
            }

            if (b is >= (byte)'0' and <= (byte)'9')
            {
                while (p < bytes.Length && bytes[p] is >= (byte)'0' and <= (byte)'9')
                {
                    p++;
                }

                result.Add(new(ScriptTokenKind.Number, Encoding.ASCII.GetString(bytes, start, p - start), start));
                continue;
            }

            if (IsIdentifierStart(b))
            {
                p++;
                while (p < bytes.Length && (IsIdentifierStart(bytes[p]) || bytes[p] is >= (byte)'0' and <= (byte)'9'))
                {
                    p++;
                }

                result.Add(new(ScriptTokenKind.Identifier, Encoding.ASCII.GetString(bytes, start, p - start).ToLowerInvariant(), start));
                continue;
            }

            if (p + 1 < bytes.Length && ((b is (byte)'=' or (byte)'!' or (byte)'<' or (byte)'>' && bytes[p + 1] == '=') || (b == '&' && bytes[p + 1] == '&')
|| (b == '|' && bytes[p + 1] == '|')))
            {
                result.Add(new(ScriptTokenKind.Symbol, Encoding.ASCII.GetString(bytes, p, 2), p));
                p += 2;
                continue;
            }

            if ("{}()+-*/%=;,<>^'`".Contains((char)b, StringComparison.Ordinal))
            {
                result.Add(new(ScriptTokenKind.Symbol, ((char)b).ToString(), p++));
                continue;
            }

            throw Error("UNKNOWN_BYTE", p);
        }

        result.Add(new(ScriptTokenKind.End, "", p));
        return result;
        ScriptException Error(string code, int offset) => new(code, member, offset, "Invalid or truncated scenario token.");
    }

    private static bool IsIdentifierStart(byte b) => b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or (byte)'_';
}
