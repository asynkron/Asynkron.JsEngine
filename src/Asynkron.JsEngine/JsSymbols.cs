namespace Asynkron.JsEngine;

/// <summary>
/// Centralised symbol definitions so parser and evaluator agree on structure.
/// </summary>
public static class JsSymbols
{
    public static readonly Symbol Program = Symbol.Intern("program");
    public static readonly Symbol Let = Symbol.Intern("let");
    public static readonly Symbol Var = Symbol.Intern("var");
    public static readonly Symbol Const = Symbol.Intern("const");
    public static readonly Symbol Function = Symbol.Intern("function");
    public static readonly Symbol Class = Symbol.Intern("class");
    public static readonly Symbol Extends = Symbol.Intern("extends");
    public static readonly Symbol Block = Symbol.Intern("block");
    public static readonly Symbol Return = Symbol.Intern("return");
    public static readonly Symbol ExpressionStatement = Symbol.Intern("expr-stmt");
    public static readonly Symbol If = Symbol.Intern("if");
    public static readonly Symbol While = Symbol.Intern("while");
    public static readonly Symbol DoWhile = Symbol.Intern("do-while");
    public static readonly Symbol For = Symbol.Intern("for");
    public static readonly Symbol ForIn = Symbol.Intern("for-in");
    public static readonly Symbol ForOf = Symbol.Intern("for-of");
    public static readonly Symbol ForAwaitOf = Symbol.Intern("for-await-of");
    public static readonly Symbol Switch = Symbol.Intern("switch");
    public static readonly Symbol Case = Symbol.Intern("case");
    public static readonly Symbol Default = Symbol.Intern("default");
    public static readonly Symbol Try = Symbol.Intern("try");
    public static readonly Symbol Catch = Symbol.Intern("catch");
    public static readonly Symbol Throw = Symbol.Intern("throw");
    public static readonly Symbol Break = Symbol.Intern("break");
    public static readonly Symbol Continue = Symbol.Intern("continue");
    public static readonly Symbol Label = Symbol.Intern("label");
    public static readonly Symbol EmptyStatement = Symbol.Intern("empty-stmt");
    public static readonly Symbol Assign = Symbol.Intern("assign");
    public static readonly Symbol Call = Symbol.Intern("call");
    public static readonly Symbol OptionalCall = Symbol.Intern("optional-call");
    public static readonly Symbol Negate = Symbol.Intern("negate");
    public static readonly Symbol UnaryPlus = Symbol.Intern("unary-plus");
    public static readonly Symbol Not = Symbol.Intern("not");
    public static readonly Symbol Typeof = Symbol.Intern("typeof");
    public static readonly Symbol Void = Symbol.Intern("void");
    public static readonly Symbol Delete = Symbol.Intern("delete");
    public static readonly Symbol Undefined = Symbol.Intern("undefined");
    public static readonly Symbol Lambda = Symbol.Intern("lambda");
    public static readonly Symbol ObjectLiteral = Symbol.Intern("object");
    public static readonly Symbol ArrayLiteral = Symbol.Intern("array");
    public static readonly Symbol Property = Symbol.Intern("prop");
    public static readonly Symbol Method = Symbol.Intern("method");
    public static readonly Symbol Getter = Symbol.Intern("getter");
    public static readonly Symbol Setter = Symbol.Intern("setter");
    public static readonly Symbol PrivateField = Symbol.Intern("private-field");
    public static readonly Symbol PublicField = Symbol.Intern("public-field");
    public static readonly Symbol StaticMethod = Symbol.Intern("static-method");
    public static readonly Symbol StaticGetter = Symbol.Intern("static-getter");
    public static readonly Symbol StaticSetter = Symbol.Intern("static-setter");
    public static readonly Symbol StaticField = Symbol.Intern("static-field");
    public static readonly Symbol GetProperty = Symbol.Intern("get-prop");
    public static readonly Symbol OptionalGetProperty = Symbol.Intern("optional-get-prop");
    public static readonly Symbol SetProperty = Symbol.Intern("set-prop");
    public static readonly Symbol GetIndex = Symbol.Intern("get-index");
    public static readonly Symbol OptionalGetIndex = Symbol.Intern("optional-get-index");
    public static readonly Symbol SetIndex = Symbol.Intern("set-index");
    public static readonly Symbol This = Symbol.Intern("this");
    public static readonly Symbol Super = Symbol.Intern("super");
    public static readonly Symbol New = Symbol.Intern("new");
    public static readonly Symbol Ternary = Symbol.Intern("ternary");
    public static readonly Symbol TemplateLiteral = Symbol.Intern("template");
    public static readonly Symbol TaggedTemplate = Symbol.Intern("tagged-template");
    public static readonly Symbol Spread = Symbol.Intern("spread");
    public static readonly Symbol Rest = Symbol.Intern("rest");
    public static readonly Symbol Uninitialized = Symbol.Intern("<uninitialized>");

    // CPS-related symbols for continuation-passing style transformation
    public static readonly Symbol Continuation = Symbol.Intern("continuation");
    public static readonly Symbol CallK = Symbol.Intern("call-k");
    public static readonly Symbol LetK = Symbol.Intern("let-k");

    // Async/Await symbols
    public static readonly Symbol Async = Symbol.Intern("async");
    public static readonly Symbol AsyncExpr = Symbol.Intern("async-expr");
    public static readonly Symbol Await = Symbol.Intern("await");
    public static readonly Symbol Promise = Symbol.Intern("promise");

    // Generator symbols
    public static readonly Symbol Generator = Symbol.Intern("generator");
    public static readonly Symbol Yield = Symbol.Intern("yield");
    public static readonly Symbol YieldStar = Symbol.Intern("yield*");

    // Suspension/Resumption symbols
    public static readonly Symbol Suspend = Symbol.Intern("suspend");
    public static readonly Symbol Resume = Symbol.Intern("resume");

    // Destructuring symbols
    public static readonly Symbol ArrayPattern = Symbol.Intern("array-pattern");
    public static readonly Symbol ObjectPattern = Symbol.Intern("object-pattern");
    public static readonly Symbol PatternElement = Symbol.Intern("pattern-element");
    public static readonly Symbol PatternProperty = Symbol.Intern("pattern-property");
    public static readonly Symbol PatternRest = Symbol.Intern("pattern-rest");
    public static readonly Symbol PatternDefault = Symbol.Intern("pattern-default");
    public static readonly Symbol DestructuringAssignment = Symbol.Intern("destructuring-assignment");

    // Module symbols
    public static readonly Symbol Import = Symbol.Intern("import");
    public static readonly Symbol DynamicImport = Symbol.Intern("dynamic-import");
    public static readonly Symbol ImportDefault = Symbol.Intern("import-default");
    public static readonly Symbol ImportNamed = Symbol.Intern("import-named");
    public static readonly Symbol ImportNamespace = Symbol.Intern("import-namespace");
    public static readonly Symbol Export = Symbol.Intern("export");
    public static readonly Symbol ExportDefault = Symbol.Intern("export-default");
    public static readonly Symbol ExportNamed = Symbol.Intern("export-named");
    public static readonly Symbol ExportDeclaration = Symbol.Intern("export-declaration");
    public static readonly Symbol From = Symbol.Intern("from");
    public static readonly Symbol As = Symbol.Intern("as");

    // Strict mode
    public static readonly Symbol UseStrict = Symbol.Intern("use-strict");

    public static Symbol Operator(string op)
    {
        return Symbol.Intern(op);
    }
}