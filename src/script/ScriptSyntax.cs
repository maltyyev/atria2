namespace Atria.Script;

public sealed record ScriptCallSite(string Caller, string Callee, int ByteOffset, bool IsBuiltin, bool HasDefinition);
public sealed record ScriptIgnoredToken(string Function, int ByteOffset, ScriptTokenKind Kind);
public sealed record ScriptSyntaxReport(IReadOnlyList<ScriptException> Errors, IReadOnlyList<ScriptCallSite> Calls,
    IReadOnlyList<ScriptIgnoredToken> IgnoredStatementTokens);

/// <summary>Read-only body validation, never invokes game callbacks or mutates variables.</summary>
public static class ScriptSyntax
{
    public static ScriptSyntaxReport Analyze(ScriptProgram program)
    {
        var errors = new List<ScriptException>();
        var calls = new List<ScriptCallSite>();
        var ignored = new List<ScriptIgnoredToken>();
        foreach (ScriptFunction function in program.Functions.Values)
        {
            var parser = new BodyParser(program, function, calls, ignored);
            try { parser.Parse(); }
            catch (ScriptException error) { errors.Add(error); }
        }
        return new(errors.AsReadOnly(), calls.AsReadOnly(), ignored.AsReadOnly());
    }

    private sealed class BodyParser(ScriptProgram program, ScriptFunction function, List<ScriptCallSite> calls,
        List<ScriptIgnoredToken> ignored)
    {
        private int cursor = function.BodyToken;
        private int depth;
        private ScriptToken Current => program.Tokens[Math.Min(cursor, program.Tokens.Count - 1)];
        private ScriptException Error(string code, string detail) => new(code, program.MemberName, Current.Offset, $"In {function.Name}: {detail}");
        private bool Take(string text) { if (Current.Text != text) return false; cursor++; return true; }
        private void Expect(string text) { if (!Take(text)) throw Error("SYNTAX", $"Expected '{text}', got '{Current.Text}'."); }
        public void Parse()
        {
            Block();
            if (cursor != function.EndToken + 1) throw Error("BODY_END", "Body parser/index disagree.");
        }
        private void Block()
        {
            Expect("{");
            if (++depth > 128) throw Error("NESTING_LIMIT", "Excessive statement nesting.");
            while (!Take("}"))
            {
                if (Current.Kind == ScriptTokenKind.End) throw Error("UNCLOSED_BLOCK", "End of member in body.");
                Statement();
            }
            depth--;
        }
        private void Statement()
        {
            if (Current.Text == "{") { Block(); return; }
            if (Take("int") || Take("char"))
            {
                do { Identifier(); } while (Take(","));
                Expect(";");
            }
            else if (Take("if"))
            {
                Expect("("); Expression(); Expect(")"); Block();
                if (Take("else")) Block();
            }
            else if (Take("while")) { Expect("("); Expression(); Expect(")"); Block(); }
            else if (Take("for"))
            {
                Expect("("); if (Current.Text != ";") Expression(); Expect(";");
                if (Current.Text != ";") Expression(); Expect(";");
                if (Current.Text != ")") Expression(); Expect(")"); Block();
            }
            else if (Take("return"))
            {
                if (Current.Text == "(" && program.Tokens[cursor + 1].Text == ")") cursor += 2;
                else if (Current.Text != ";") Expression();
                Expect(";");
            }
            else if (!Take(";"))
            {
                if (Current.Kind == ScriptTokenKind.Identifier) { Expression(); Expect(";"); }
                else { ignored.Add(new(function.Name, Current.Offset, Current.Kind)); cursor++; }
            }
        }
        private string Identifier()
        {
            if (Current.Kind != ScriptTokenKind.Identifier) throw Error("SYNTAX", "Expected identifier.");
            return program.Tokens[cursor++].Text;
        }
        private void Expression()
        {
            if (++depth > 128) throw Error("NESTING_LIMIT", "Excessive expression nesting.");
            if (Current.Kind == ScriptTokenKind.Identifier && program.Tokens[cursor + 1].Text == "=")
            { cursor += 2; Expression(); }
            else
            {
                Comparison();
                while (Take("&&") || Take("||")) Comparison();
            }
            depth--;
        }
        private void Comparison()
        {
            Additive();
            if (Current.Text is "<" or ">" or "<=" or ">=" or "==" or "!=") { cursor++; Additive(); }
        }
        private void Additive() { Product(); while (Take("+") || Take("-")) Product(); }
        private void Product() { Unary(); while (Take("*") || Take("/")) Unary(); }
        private void Unary()
        {
            if (!Take("+")) Take("-");
            if (Take("(")) { Expression(); Expect(")"); return; }
            if (Current.Kind == ScriptTokenKind.Number) { cursor++; return; }
            ScriptToken name = Current;
            Identifier();
            if (!Take("(")) return;
            calls.Add(new(function.Name, name.Text, name.Offset, ScriptMachine.BuiltinNames.Contains(name.Text), program.Functions.ContainsKey(name.Text)));
            if (name.Text is "people_name" or "dungeon_enter" or "dialogue_script" or "question_dialog")
            {
                if (name.Text is "dialogue_script" or "question_dialog") { Expression(); Expect(","); Expression(); Expect(","); }
                while (!Take(")"))
                {
                    if (Current.Kind != ScriptTokenKind.String) throw Error("SYNTAX", "Expected raw string-list token.");
                    cursor++;
                }
            }
            else if (!Take(")"))
            {
                do { if (Current.Kind == ScriptTokenKind.String) cursor++; else Expression(); } while (Take(","));
                Expect(")");
            }
        }
    }
}
