namespace Atria.Script;

public readonly struct ScriptValue
{
    public bool IsString
    {
        get;
    }
    public int Integer
    {
        get;
    }
    public ReadOnlyMemory<byte> Bytes
    {
        get;
    }

    public ScriptValue(int integer) => (IsString, Integer, Bytes) = (false, integer, default);
    public ScriptValue(ReadOnlyMemory<byte> bytes) => (IsString, Integer, Bytes) = (true, 0, bytes);
    public int RequireInteger() => IsString ? throw new InvalidOperationException("A scenario string is not an integer.") : Integer;
}

public sealed record ScriptCall(string Member, string Function, int ByteOffset, IReadOnlyList<ScriptValue> Arguments);
/// <summary>Only evidence-backed game callbacks belong here. Missing callbacks must fail.</summary>
public interface IScriptHost
{
    int Invoke(ScriptCall call);
}

/// <summary>Native mode1100 callbacks skip argument tokens without evaluating expressions.</summary>
public interface IScriptArgumentPolicy
{
    bool SkipArguments(string builtinName);
}

public sealed class UnresolvedScriptHost : IScriptHost
{
    public int Invoke(ScriptCall call) => throw new ScriptException("UNIMPLEMENTED_BUILTIN", call.Member, call.ByteOffset, $"Game callback '{call.Function}' has not been recovered/implemented.");
}

/// <summary>
/// Bounded scalar interpreter of the recovered scenario language. Original addresses:
/// assignment 43b890; boolean 43b960; comparison 43ba00; arithmetic 43bb10..43bc10;
/// statement dispatch 43c060; calls/locals 43c270..43c5d0.
/// </summary>
public sealed class ScriptMachine
{
    private readonly ScriptProgram program;
    private readonly IScriptHost host;
    private readonly Dictionary<string, int> globals = new(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, int>> locals = [];
    private int cursor, frameBase, depth, steps, returnRegister, evaluationDepth;
    public int StepLimit { get; set; } = 1_000_000;
    public int LastStepCount => steps;
    public IReadOnlyDictionary<string, int> Globals => globals;

    public ScriptMachine(ScriptProgram program, IScriptHost host)
    {
        this.program = program;
        this.host = host;
        foreach (string name in program.GlobalVariables)
        {
            globals.TryAdd(name, 0);
        }
    }

    public int Invoke(string function, params int[] arguments)
    {
        if (depth != 0)
        {
            throw new InvalidOperationException("Use a separate machine for external reentry.");
        }

        steps = 0;
        return CallFunction(function.ToLowerInvariant(), arguments);
    }

    private ScriptToken Current => program.Tokens[Math.Min(cursor, program.Tokens.Count - 1)];

    private ScriptException Error(string code, string message) => new(code, program.MemberName, Current.Offset, message);
    private void Tick()
    {
        if (++steps > StepLimit)
        {
            throw Error("STEP_LIMIT", "Scenario exceeded its bounded execution budget.");
        }
    }

    private bool Take(string text)
    {
        if (Current.Text != text)
        {
            return false;
        }

        cursor++;
        return true;
    }

    private void Expect(string text)
    {
        if (!Take(text))
        {
            throw Error("EXPECTED_TOKEN", $"Expected '{text}', got '{Current.Text}'.");
        }
    }

    private int CallFunction(string name, int[] args)
    {
        if (!program.Functions.TryGetValue(name, out ScriptFunction? function))
        {
            throw Error("MISSING_FUNCTION", $"Scenario handler '{name}' is absent; no substitute was executed.");
        }

        if (args.Length != function.Parameters.Length)
        {
            throw Error("ARGUMENT_COUNT", $"Wrong argument count for '{name}'.");
        }

        if (depth >= 128)
        {
            throw Error("CALL_DEPTH", "Scenario recursion exceeded the native safety limit.");
        }

        int oldCursor = cursor, oldBase = frameBase, oldCount = locals.Count;
        depth++;
        frameBase = oldCount;
        for (int i = 0; i < args.Length; i++)
        {
            locals.Add(new(function.Parameters[i], args[i]));
        }

        cursor = function.BodyToken;
        try
        {
            ExecuteFrame();
            // The original return register is shared and is not zeroed on function entry.
            return returnRegister;
        }
        finally
        {
            locals.RemoveRange(oldCount, locals.Count - oldCount);
            frameBase = oldBase;
            cursor = oldCursor;
            depth--;
        }
    }

    // Deliberately cursor-based: native return exits the current 43c060 invocation,
    // not all enclosing blocks. An AST evaluator with ordinary C return changes behavior.
    private void ExecuteFrame()
    {
        if (++evaluationDepth > 128)
        {
            evaluationDepth--;
            throw Error("NESTING_LIMIT", "Scenario evaluation nesting exceeded safety limit.");
        }

        try
        {
            ExecuteFrameCore();
        }
        finally
        {
            evaluationDepth--;
        }
    }

    private void ExecuteFrameCore()
    {
        bool block = false;
        do
        {
            Tick();
            ScriptToken token = Current;
            if (token.Kind == ScriptTokenKind.End)
            {
                throw Error("UNEXPECTED_EOF", "Execution reached member end inside a function.");
            }

            if (Take("{"))
            {
                block = true;
                continue;
            }

            if (Take("}"))
            {
                return;
            }

            if (Take("int") || Take("char"))
            {
                do
                {
                    if (Current.Kind != ScriptTokenKind.Identifier)
                    {
                        throw Error("EXPECTED_IDENTIFIER", "Local variable name required.");
                    }

                    locals.Add(new(Current.Text, 0));
                    cursor++;
                    if (locals.Count > 300)
                    {
                        throw Error("LOCAL_LIMIT", "Scenario local-variable stack exceeds original capacity.");
                    }
                }
                while (Take(","));
                Expect(";");
            }
            else if (Take("if"))
            {
                int condition = Expression();
                if (condition == 0)
                {
                    SkipBlock();
                    if (Take("else"))
                    {
                        ExecuteFrame();
                    }
                }
                else
                {
                    ExecuteFrame();
                }
            }
            else if (Take("else"))
            {
                SkipBlock();
            }
            else if (Take("while"))
            {
                int conditionStart = cursor;
                int condition = Expression();
                if (condition == 0)
                {
                    SkipBlock();
                }
                else
                {
                    ExecuteFrame();
                    cursor = conditionStart - 1;
                }
            }
            else if (Take("for"))
            {
                Expect("(");
                Expression();
                Expect(";");
                int conditionStart = cursor;
                while (true)
                {
                    cursor = conditionStart;
                    int condition = Expression();
                    Expect(";");
                    int increment = cursor;
                    int nesting = 1;
                    while (nesting != 0)
                    {
                        Tick();
                        if (Current.Kind == ScriptTokenKind.End)
                        {
                            throw Error("UNEXPECTED_EOF", "Unclosed for header.");
                        }

                        if (Take("("))
                        {
                            nesting++;
                        }
                        else if (Take(")"))
                        {
                            nesting--;
                        }
                        else
                        {
                            cursor++;
                        }
                    }

                    if (condition == 0)
                    {
                        SkipBlock();
                        break;
                    }

                    ExecuteFrame();
                    cursor = increment;
                    Expression();
                }
            }
            else if (Take("return"))
            {
                if (Current.Text == "(" && program.Tokens[cursor + 1].Text == ")")
                {
                    cursor += 2;
                    returnRegister = 0;
                }
                else
                {
                    returnRegister = Current.Text == ";" ? 0 : Expression();
                }
                // Leave ';' unread, exactly as b850/putback does before c410 returns.
                return;
            }
            else if (Take(";"))
            {
            }
            else if (token.Kind == ScriptTokenKind.Identifier)
            {
                Expression();
                Expect(";");
            }
            else
            {
                // 43c060 ignores tokens with neither identifier type nor a dispatched
                // keyword code. Shipped SUBEVENT contains stray string and quote tokens.
                cursor++;
            }
        }
        while (block);
    }

    private void SkipBlock()
    {
        if (!program.BlockEnds.TryGetValue(cursor, out int end))
        {
            throw Error("EXPECTED_BLOCK", "Original false-branch scanner requires a braced block.");
        }

        cursor = end + 1;
    }

    private int Expression()
    {
        if (++evaluationDepth > 128)
        {
            evaluationDepth--;
            throw Error("NESTING_LIMIT", "Scenario evaluation nesting exceeded safety limit.");
        }

        try
        {
            return ExpressionCore();
        }
        finally
        {
            evaluationDepth--;
        }
    }

    private int ExpressionCore()
    {
        Tick();
        if (Current.Text == ";")
        {
            return 0;
        }

        if (Current.Kind == ScriptTokenKind.Identifier && cursor + 1 < program.Tokens.Count && program.Tokens[cursor + 1].Text == "="
&& HasVariable(Current.Text))
        {
            string name = Current.Text;
            cursor += 2;
            int value = Expression();
            SetVariable(name, value);
            return value;
        }

        int left = Comparison();
        if (Current.Text is "&&" or "||")
        {
            // 43b960 captures the first operator once, evaluates every RHS eagerly,
            // and retains that operator even if later connective tokens differ.
            string op = Current.Text;
            int count = 0;
            do
            {
                cursor++;
                int right = Comparison();
                left = op == "&&" ? (left != 0 && right != 0 ? 1 : 0) : (left != 0 || right != 0 ? 1 : 0);
                if (Current.Text == ")")
                {
                    break;
                }

                if (++count >= 100 || Current.Text is not ("&&" or "||"))
                {
                    throw Error("BOOLEAN_TERMINATOR", "Recovered boolean evaluator requires a closing parenthesis and at most 100 operands.");
                }
            }
            while (true);
        }

        return left;
    }

    private int Comparison()
    {
        int left = Additive();
        string op = Current.Text;
        if (op is not ("<" or "<=" or ">" or ">=" or "==" or "!="))
        {
            return left;
        }

        cursor++;
        int right = Additive();
        return (op switch
        {
            "<" => left < right,
            "<=" => left <= right,
            ">" => left > right,
            ">=" => left >= right,
            "==" => left == right,
            _ => left != right
        }) ? 1 : 0;
    }

    private int Additive()
    {
        int value = Multiplicative();
        while (Current.Text is "+" or "-")
        {
            string op = Current.Text;
            cursor++;
            int right = Multiplicative();
            value = op == "+" ? unchecked(value + right) : unchecked(value - right);
        }

        return value;
    }

    private int Multiplicative()
    {
        int value = Unary();
        while (Current.Text is "*" or "/")
        {
            string op = Current.Text;
            cursor++;
            int right = Unary();
            if (op == "*")
            {
                value = unchecked(value * right);
            }
            else
            {
                if (right == 0 || value == int.MinValue && right == -1)
                {
                    throw Error("INVALID_DIVISION", "Scenario integer division would fault in the original executable.");
                }

                value /= right;
            }
        }

        return value;
    }

    private int Unary()
    {
        bool minus = Take("-");
        if (!minus)
        {
            Take("+");
        }

        int value = Primary();
        return minus ? unchecked(-value) : value;
    }

    private int Primary()
    {
        Tick();
        if (Take("("))
        {
            int value = Expression();
            Expect(")");
            return value;
        }

        ScriptToken token = Current;
        if (token.Kind == ScriptTokenKind.Number)
        {
            cursor++;
            if (!int.TryParse(token.Text, out int value))
            {
                throw Error("INTEGER_RANGE", "Decimal literal exceeds signed 32-bit range.");
            }

            return value;
        }

        if (token.Kind != ScriptTokenKind.Identifier)
        {
            throw Error("EXPECTED_EXPRESSION", "Expected scalar expression.");
        }

        cursor++;
        if (Take("("))
        {
            var args = new List<ScriptValue>();
            if (BuiltinNames.Contains(token.Text) && host is IScriptArgumentPolicy policy && policy.SkipArguments(token.Text))
            {
                while (!Current.Text.Contains(')', StringComparison.Ordinal))
                {
                    Tick();
                    if (Current.Kind == ScriptTokenKind.End)
                    {
                        throw Error("UNEXPECTED_EOF", "Missing suppressed callback terminator.");
                    }

                    cursor++;
                }

                cursor++;
                return host.Invoke(new(program.MemberName, token.Text, token.Offset, args.AsReadOnly()));
            }

            if (token.Text is "people_name" or "dungeon_enter" or "dialogue_script" or "question_dialog")
            {
                // These native callbacks tokenize a whitespace-separated string tail,
                // not C-style comma-separated arguments (42d050,42d0c0,42d490).
                if (token.Text is "dialogue_script" or "question_dialog")
                {
                    args.Add(new(Expression()));
                    Expect(",");
                    args.Add(new(Expression()));
                    Expect(",");
                }

                while (!Take(")"))
                {
                    Tick();
                    if (Current.Kind != ScriptTokenKind.String)
                    {
                        throw Error("EXPECTED_STRING", "Expected callback string-list token.");
                    }

                    args.Add(new(Current.RawBytes));
                    cursor++;
                }

                if (token.Text == "people_name" && args.Count % 3 != 0)
                {
                    throw Error("PEOPLE_TRIPLES", "People declarations require display label, legacy label and handler triples.");
                }

                if (token.Text == "dungeon_enter" && args.Count != 2)
                {
                    throw Error("DUNGEON_STRINGS", "Dungeon entry requires map and script filenames.");
                }
            }
            else if (!Take(")"))
            {
                do
                {
                    if (Current.Kind == ScriptTokenKind.String)
                    {
                        args.Add(new(Current.RawBytes));
                        cursor++;
                    }
                    else
                    {
                        args.Add(new(Expression()));
                    }
                }
                while (Take(","));
                Expect(")");
            }

            if (BuiltinNames.Contains(token.Text))
            {
                return host.Invoke(new(program.MemberName, token.Text, token.Offset, args.AsReadOnly()));
            }

            int[] integers = new int[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].IsString)
                {
                    throw Error("STRING_PARAMETER", "Native user functions accept scalar parameters only.");
                }

                integers[i] = args[i].Integer;
            }

            return CallFunction(token.Text, integers);
        }

        return GetVariable(token.Text);
    }

    private bool HasVariable(string name) => FindLocal(name) >= 0 || globals.ContainsKey(name);
    private int FindLocal(string name)
    {
        for (int i = locals.Count - 1; i >= frameBase; i--)
        {
            if (locals[i].Key == name)
            {
                return i;
            }
        }

        return -1;
    }

    private int GetVariable(string name)
    {
        int index = FindLocal(name);
        if (index >= 0)
        {
            return locals[index].Value;
        }

        if (globals.TryGetValue(name, out int value))
        {
            return value;
        }

        throw Error("UNDEFINED_VARIABLE", $"Scenario variable '{name}' has not been declared.");
    }

    private void SetVariable(string name, int value)
    {
        int index = FindLocal(name);
        if (index >= 0)
        {
            locals[index] = new(name, value);
        }
        else if (globals.ContainsKey(name))
        {
            globals[name] = value;
        }
        else
        {
            throw Error("UNDEFINED_VARIABLE", $"Scenario variable '{name}' has not been declared.");
        }
    }

    public static IReadOnlySet<string> BuiltinNames
    {
        get;
    } = new HashSet<string>(StringComparer.Ordinal)
    {
        "set_global",
        "get_global",
        "set_event",
        "get_event",
        "enter_out",
        "set_eventstatus",
        "get_eventstatus",
        "people_name",
        "dialogue_script",
        "dialogue_script2",
        "eventflag_check",
        "battle_start",
        "battle_start2",
        "battle_start3",
        "question_dialog",
        "question_dialog2",
        "get_money",
        "lost_money",
        "check_money",
        "check_item",
        "get_item",
        "lost_item",
        "check_eventitem",
        "get_eventitem",
        "lost_eventitem",
        "member_inout",
        "member_check",
        "get_xp",
        "subevent_position",
        "event_count_plus",
        "get_event_count",
        "game_score_plus",
        "shop_number",
        "dungeon_enter",
        "set_daycount",
        "get_daycount",
        "change_chip",
        "set_dungeonfog",
        "newchar_create",
        "visual_play",
        "game_end",
        "script_end"
    };
}
