using System.Threading.Channels;

namespace Asynkron.JsEngine;

/// <summary>
/// High level façade that turns JavaScript source into S-expressions and evaluates them.
/// </summary>
public sealed class JsEngine
{
    private readonly Environment _global = new(isFunctionScope: true);
    private readonly CpsTransformer _cpsTransformer = new();
    private readonly Channel<Func<Task>> _eventQueue = Channel.CreateUnbounded<Func<Task>>();
    private readonly Dictionary<int, CancellationTokenSource> _timers = new();
    private readonly HashSet<Task> _activeTimerTasks = [];
    private int _nextTimerId = 1;
    
    // Module registry: maps module paths to their exported values
    private readonly Dictionary<string, JsObject> _moduleRegistry = new();
    
    // Module loader function: allows custom module loading logic
    private Func<string, string>? _moduleLoader;

    /// <summary>
    /// Initializes a new instance of JsEngine with standard library objects.
    /// </summary>
    public JsEngine()
    {
        // Register standard library objects
        SetGlobal("Math", StandardLibrary.CreateMathObject());
        SetGlobal("Number", StandardLibrary.CreateNumberConstructor());
        SetGlobal("String", StandardLibrary.CreateStringConstructor());
        SetGlobal("Object", StandardLibrary.CreateObjectConstructor());
        SetGlobal("Array", StandardLibrary.CreateArrayConstructor());
        
        // Register global constants
        SetGlobal("Infinity", double.PositiveInfinity);
        SetGlobal("NaN", double.NaN);
        SetGlobal("undefined", JsSymbols.Undefined);
        
        // Register Date constructor as a callable object with static methods
        var dateConstructor = StandardLibrary.CreateDateConstructor();
        var dateObj = StandardLibrary.CreateDateObject();
        
        // Add static methods to constructor
        if (dateConstructor is HostFunction hf)
        {
            foreach (var prop in dateObj)
            {
                hf.SetProperty(prop.Key, prop.Value);
            }
        }
        
        SetGlobal("Date", dateConstructor);
        SetGlobal("JSON", StandardLibrary.CreateJsonObject());
        
        // Register RegExp constructor
        SetGlobal("RegExp", StandardLibrary.CreateRegExpConstructor());
        
        // Register Promise constructor
        SetGlobal("Promise", StandardLibrary.CreatePromiseConstructor(this));
        
        // Register Symbol constructor
        SetGlobal("Symbol", StandardLibrary.CreateSymbolConstructor());
        
        // Register Map constructor
        SetGlobal("Map", StandardLibrary.CreateMapConstructor());
        
        // Register Set constructor
        SetGlobal("Set", StandardLibrary.CreateSetConstructor());
        
        // Register WeakMap constructor
        SetGlobal("WeakMap", StandardLibrary.CreateWeakMapConstructor());
        
        // Register WeakSet constructor
        SetGlobal("WeakSet", StandardLibrary.CreateWeakSetConstructor());
        
        // Register Error constructors
        SetGlobal("Error", StandardLibrary.CreateErrorConstructor("Error"));
        SetGlobal("TypeError", StandardLibrary.CreateErrorConstructor("TypeError"));
        SetGlobal("RangeError", StandardLibrary.CreateErrorConstructor("RangeError"));
        SetGlobal("ReferenceError", StandardLibrary.CreateErrorConstructor("ReferenceError"));
        SetGlobal("SyntaxError", StandardLibrary.CreateErrorConstructor("SyntaxError"));
        
        // Register eval function
        SetGlobal("eval", StandardLibrary.CreateEvalFunction(this));
        
        // Register timer functions
        SetGlobalFunction("setTimeout", args => SetTimeout(args));
        SetGlobalFunction("setInterval", args => SetInterval(args));
        SetGlobalFunction("clearTimeout", args => ClearTimer(args));
        SetGlobalFunction("clearInterval", args => ClearTimer(args));
        
        // Register dynamic import function
        SetGlobalFunction("import", args => DynamicImport(args));
    }

    /// <summary>
    /// Parses JavaScript source code into an S-expression representation.
    /// Applies CPS (Continuation-Passing Style) transformation if the code contains
    /// async functions, generators, await expressions, or yield expressions.
    /// </summary>
    public Cons Parse(string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        
        // Step 2: Parse to S-expressions
        var parser = new Parser(tokens);
        var program = parser.ParseProgram();
        
        // Step 3: Apply CPS transformation if needed
        // This enables support for generators and async/await by converting
        // the S-expression tree to continuation-passing style
        if (_cpsTransformer.NeedsTransformation(program))
        {
            return _cpsTransformer.Transform(program);
        }
        
        return program;
    }

    /// <summary>
    /// Parses JavaScript source code into an S-expression representation WITHOUT applying CPS transformation.
    /// This is useful for debugging and understanding the initial parse tree before transformation.
    /// </summary>
    public Cons ParseWithoutTransformation(string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        
        // Step 2: Parse to S-expressions (without transformation)
        var parser = new Parser(tokens);
        return parser.ParseProgram();
    }

    /// <summary>
    /// Parses JavaScript source code and returns both the pre-transformation and post-transformation
    /// S-expression representations. This is useful for understanding how CPS transformation affects the code.
    /// </summary>
    /// <param name="source">JavaScript source code</param>
    /// <returns>A tuple containing (original, transformed) S-expressions. If no transformation is needed, both will be the same.</returns>
    public (Cons original, Cons transformed) ParseWithTransformationSteps(string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        
        // Step 2: Parse to S-expressions
        var parser = new Parser(tokens);
        var original = parser.ParseProgram();
        
        // Step 3: Apply CPS transformation if needed
        Cons transformed;
        if (_cpsTransformer.NeedsTransformation(original))
        {
            transformed = _cpsTransformer.Transform(original);
        }
        else
        {
            transformed = original;
        }
        
        return (original, transformed);
    }

    /// <summary>
    /// Parses and immediately evaluates the provided source.
    /// </summary>
    public object? Evaluate(string source)
        => Evaluate(Parse(source));

    /// <summary>
    /// Evaluates an S-expression program.
    /// </summary>
    public object? Evaluate(Cons program)
    {
        // Check if the program contains any import/export statements
        if (HasModuleStatements(program))
        {
            // Treat as a module
            var exports = new JsObject();
            return EvaluateModule(program, _global, exports);
        }
        
        return Evaluator.EvaluateProgram(program, _global);
    }
    
    /// <summary>
    /// Checks if a program contains any import or export statements.
    /// </summary>
    private bool HasModuleStatements(Cons program)
    {
        if (program.Head is not Symbol head || !ReferenceEquals(head, JsSymbols.Program))
        {
            return false;
        }
        
        foreach (var stmt in program.Rest)
        {
            if (stmt is Cons { Head: Symbol stmtHead })
            {
                if (ReferenceEquals(stmtHead, JsSymbols.Import) ||
                    ReferenceEquals(stmtHead, JsSymbols.Export) ||
                    ReferenceEquals(stmtHead, JsSymbols.ExportDefault) ||
                    ReferenceEquals(stmtHead, JsSymbols.ExportNamed) ||
                    ReferenceEquals(stmtHead, JsSymbols.ExportDeclaration))
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Registers a value in the global scope.
    /// </summary>
    public void SetGlobal(string name, object? value)
        => _global.Define(Symbol.Intern(name), value);

    /// <summary>
    /// Registers a host function that can be invoked from interpreted code.
    /// </summary>
    public void SetGlobalFunction(string name, Func<IReadOnlyList<object?>, object?> handler)
        => _global.Define(Symbol.Intern(name), new HostFunction(handler));

    /// <summary>
    /// Registers a host function that receives the <c>this</c> binding.
    /// </summary>
    public void SetGlobalFunction(string name, Func<object?, IReadOnlyList<object?>, object?> handler)
        => _global.Define(Symbol.Intern(name), new HostFunction(handler));

    /// <summary>
    /// Parses and evaluates the provided source code, then processes any scheduled events
    /// in the event queue. The engine will continue running until the queue is empty.
    /// </summary>
    /// <param name="source">The JavaScript source code to execute</param>
    /// <returns>A task that completes when all scheduled events have been processed</returns>
    public async Task<object?> Run(string source)
    {
        // Parse and evaluate the source code
        var result = Evaluate(source);
        
        // Process the event queue until it's empty
        await ProcessEventQueue();
        
        return result;
    }

    /// <summary>
    /// Schedules a task to be executed on the event queue.
    /// This allows promises and other async operations to schedule work.
    /// </summary>
    /// <param name="task">The task to schedule</param>
    public void ScheduleTask(Func<Task> task)
    {
        _eventQueue.Writer.TryWrite(task);
    }

    /// <summary>
    /// Processes all events in the event queue until it's empty.
    /// Each event is executed and any new events scheduled during execution
    /// will also be processed.
    /// </summary>
    private async Task ProcessEventQueue()
    {
        while (_eventQueue.Reader.TryRead(out var task))
        {
            await task();
        }

        // Wait for any active timer tasks to schedule their work
        // We poll multiple times to handle scenarios where timers schedule other timers
        for (int i = 0; i < 50 && _activeTimerTasks.Count > 0; i++)
        {
            await Task.Delay(20);
            
            // Process any newly scheduled events
            while (_eventQueue.Reader.TryRead(out var task))
            {
                await task();
            }
            
            // If there are no more active tasks, we can stop
            bool hasActiveTasks;
            lock (_activeTimerTasks)
            {
                hasActiveTasks = _activeTimerTasks.Count > 0;
            }
            
            if (!hasActiveTasks)
                break;
        }
    }

    /// <summary>
    /// Implements setTimeout - schedules a callback to run after a delay.
    /// </summary>
    private object? SetTimeout(IReadOnlyList<object?> args)
    {
        if (args.Count < 2 || args[0] is not IJsCallable callback)
            return null;

        var delay = args[1] is double d ? (int)d : 0;
        var timerId = _nextTimerId++;
        
        var cts = new CancellationTokenSource();
        _timers[timerId] = cts;

        Task? timerTask = null;
        timerTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                
                if (!cts.Token.IsCancellationRequested)
                {
                    ScheduleTask(() =>
                    {
                        callback.Invoke([], null);
                        return Task.CompletedTask;
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // Timer was cancelled
            }
            finally
            {
                _timers.Remove(timerId);
                if (timerTask != null)
                {
                    lock (_activeTimerTasks)
                    {
                        _activeTimerTasks.Remove(timerTask);
                    }
                }
            }
        }, cts.Token);

        lock (_activeTimerTasks)
        {
            _activeTimerTasks.Add(timerTask);
        }

        return (double)timerId;
    }

    /// <summary>
    /// Implements setInterval - schedules a callback to run repeatedly at a fixed interval.
    /// </summary>
    private object? SetInterval(IReadOnlyList<object?> args)
    {
        if (args.Count < 2 || args[0] is not IJsCallable callback)
            return null;

        var interval = args[1] is double d ? (int)d : 0;
        var timerId = _nextTimerId++;
        
        var cts = new CancellationTokenSource();
        _timers[timerId] = cts;

        Task? timerTask = null;
        timerTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(interval, cts.Token);
                    
                    if (!cts.Token.IsCancellationRequested)
                    {
                        ScheduleTask(() =>
                        {
                            callback.Invoke([], null);
                            return Task.CompletedTask;
                        });
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Timer was cancelled
            }
            finally
            {
                _timers.Remove(timerId);
                if (timerTask != null)
                {
                    lock (_activeTimerTasks)
                    {
                        _activeTimerTasks.Remove(timerTask);
                    }
                }
            }
        }, cts.Token);

        lock (_activeTimerTasks)
        {
            _activeTimerTasks.Add(timerTask);
        }

        return (double)timerId;
    }

    /// <summary>
    /// Implements clearTimeout/clearInterval - cancels a timer.
    /// </summary>
    private object? ClearTimer(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not double timerId)
            return null;

        var id = (int)timerId;
        if (_timers.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            _timers.Remove(id);
        }

        return null;
    }
    
    /// <summary>
    /// Implements dynamic import() - loads a module and returns a Promise that resolves to the module's exports.
    /// </summary>
    private object? DynamicImport(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            throw new Exception("import() requires a module specifier");
        }

        var modulePath = args[0]?.ToString();
        if (string.IsNullOrEmpty(modulePath))
        {
            throw new Exception("import() requires a valid module specifier");
        }

        // Create a promise that will resolve with the module exports
        var promise = new JsPromise(this);
        var promiseObj = promise.JsObject;
        
        // Add promise instance methods (then, catch, finally)
        StandardLibrary.AddPromiseInstanceMethods(promiseObj, promise, this);
        
        // Schedule loading the module asynchronously
        _eventQueue.Writer.TryWrite(async () =>
        {
            try
            {
                // Load the module synchronously (it's cached if already loaded)
                var exports = LoadModule(modulePath);
                promise.Resolve(exports);
            }
            catch (Exception ex)
            {
                promise.Reject(ex.Message);
            }
            
            await Task.CompletedTask;
        });

        return promiseObj;
    }
    
    /// <summary>
    /// Sets a custom module loader function that will be called to load module source code.
    /// The function receives the module path and should return the module source code.
    /// If not set, the engine will use File.ReadAllText to load modules from the file system.
    /// </summary>
    public void SetModuleLoader(Func<string, string> loader)
    {
        _moduleLoader = loader;
    }
    
    /// <summary>
    /// Loads and evaluates a module, returning its exports object.
    /// If the module has already been loaded, returns the cached exports.
    /// </summary>
    internal JsObject LoadModule(string modulePath)
    {
        // Check if module is already loaded
        if (_moduleRegistry.TryGetValue(modulePath, out var cachedExports))
        {
            return cachedExports;
        }
        
        // Load module source
        string source;
        if (_moduleLoader != null)
        {
            source = _moduleLoader(modulePath);
        }
        else
        {
            // Default: load from file system
            source = File.ReadAllText(modulePath);
        }
        
        // Parse the module
        var program = Parse(source);
        
        // Create a module exports object
        var exports = new JsObject();
        
        // Create a module environment (inherits from global)
        var moduleEnv = new Environment(_global, isFunctionScope: false);
        
        // Evaluate the module with export tracking
        EvaluateModule(program, moduleEnv, exports);
        
        // Cache the exports
        _moduleRegistry[modulePath] = exports;
        
        return exports;
    }
    
    /// <summary>
    /// Evaluates a module program and populates the exports object.
    /// Returns the last evaluated value.
    /// </summary>
    private object? EvaluateModule(Cons program, Environment moduleEnv, JsObject exports)
    {
        if (program.Head is not Symbol head || !ReferenceEquals(head, JsSymbols.Program))
        {
            throw new InvalidOperationException("Expected program node");
        }
        
        object? lastValue = null;
        var statements = program.Rest;
        while (statements is not null && !statements.IsEmpty)
        {
            var stmt = statements.Head;
            
            if (stmt is Cons { Head: Symbol stmtHead } stmtCons)
            {
                if (ReferenceEquals(stmtHead, JsSymbols.Import))
                {
                    // Process import statement
                    EvaluateImport(stmtCons, moduleEnv);
                }
                else if (ReferenceEquals(stmtHead, JsSymbols.ExportDefault))
                {
                    // export default expression
                    var expression = stmtCons.Rest.Head;
                    
                    // Evaluate the expression and export it as default
                    // For function/class declarations with names, this will define them and return them
                    // For expressions, this will just evaluate them
                    object? value;
                    
                    if (expression is Cons { Head: Symbol exprHead } exprCons)
                    {
                        if (ReferenceEquals(exprHead, JsSymbols.Function) || 
                            ReferenceEquals(exprHead, JsSymbols.Class))
                        {
                            // It's a named function or class declaration
                            // Evaluate it to define it in the environment
                            var declProgram = Cons.FromEnumerable([JsSymbols.Program, expression]);
                            Evaluator.EvaluateProgram(declProgram, moduleEnv);
                            
                            // Get the defined value from the environment
                            var name = exprCons.Rest.Head as Symbol;
                            if (name != null)
                            {
                                value = moduleEnv.Get(name);
                            }
                            else
                            {
                                // Shouldn't happen, but handle it
                                value = null;
                            }
                        }
                        else
                        {
                            // It's some other construct - evaluate it as an expression
                            var exprProgram = Cons.FromEnumerable([JsSymbols.Program, 
                                Cons.FromEnumerable([JsSymbols.ExpressionStatement, expression])]);
                            value = Evaluator.EvaluateProgram(exprProgram, moduleEnv);
                        }
                    }
                    else
                    {
                        // It's a symbol or literal - evaluate it as an expression
                        var exprProgram = Cons.FromEnumerable([JsSymbols.Program, 
                            Cons.FromEnumerable([JsSymbols.ExpressionStatement, expression])]);
                        value = Evaluator.EvaluateProgram(exprProgram, moduleEnv);
                    }
                    
                    exports["default"] = value;
                }
                else if (ReferenceEquals(stmtHead, JsSymbols.ExportNamed))
                {
                    // export { name1, name2 }
                    var exportList = stmtCons.Rest.Head as Cons;
                    var fromModule = stmtCons.Rest.Rest.Head as string;
                    
                    if (fromModule != null)
                    {
                        // Re-export from another module
                        var sourceExports = LoadModule(fromModule);
                        
                        if (exportList != null)
                        {
                            foreach (var exportItem in exportList)
                            {
                                if (exportItem is Cons { Head: Symbol exportHead } exportCons &&
                                    ReferenceEquals(exportHead, JsSymbols.ExportNamed))
                                {
                                    var local = exportCons.Rest.Head as Symbol;
                                    var exported = exportCons.Rest.Rest.Head as Symbol;
                                    
                                    if (local != null && exported != null)
                                    {
                                        var localName = local.Name;
                                        var exportedName = exported.Name;
                                        
                                        if (sourceExports.TryGetValue(localName, out var value))
                                        {
                                            exports[exportedName] = value;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (exportList != null)
                    {
                        // Export from current module
                        foreach (var exportItem in exportList)
                        {
                            if (exportItem is Cons { Head: Symbol exportHead } exportCons &&
                                ReferenceEquals(exportHead, JsSymbols.ExportNamed))
                            {
                                var local = exportCons.Rest.Head as Symbol;
                                var exported = exportCons.Rest.Rest.Head as Symbol;
                                
                                if (local != null && exported != null)
                                {
                                    var localName = local.Name;
                                    var exportedName = exported.Name;
                                    var value = moduleEnv.Get(local);
                                    exports[exportedName] = value;
                                }
                            }
                        }
                    }
                }
                else if (ReferenceEquals(stmtHead, JsSymbols.ExportDeclaration))
                {
                    // export let/const/var/function/class
                    var declaration = stmtCons.Rest.Head;
                    
                    // Evaluate the declaration
                    var declProgram = Cons.FromEnumerable([JsSymbols.Program, declaration]);
                    Evaluator.EvaluateProgram(declProgram, moduleEnv);
                    
                    // Extract the declared names and add to exports
                    if (declaration is Cons { Head: Symbol declHead } declCons)
                    {
                        if (ReferenceEquals(declHead, JsSymbols.Let) || 
                            ReferenceEquals(declHead, JsSymbols.Const) || 
                            ReferenceEquals(declHead, JsSymbols.Var))
                        {
                            // Variable declaration: (let name value) or (let name)
                            var name = declCons.Rest.Head as Symbol;
                            if (name != null)
                            {
                                var value = moduleEnv.Get(name);
                                exports[name.Name] = value;
                            }
                        }
                        else if (ReferenceEquals(declHead, JsSymbols.Function) || 
                                 ReferenceEquals(declHead, JsSymbols.Async) ||
                                 ReferenceEquals(declHead, JsSymbols.Generator))
                        {
                            // Function declaration: (function name params body)
                            var name = declCons.Rest.Head as Symbol;
                            if (name != null)
                            {
                                var value = moduleEnv.Get(name);
                                exports[name.Name] = value;
                            }
                        }
                        else if (ReferenceEquals(declHead, JsSymbols.Class))
                        {
                            // Class declaration: (class name ...)
                            var name = declCons.Rest.Head as Symbol;
                            if (name != null)
                            {
                                var value = moduleEnv.Get(name);
                                exports[name.Name] = value;
                            }
                        }
                    }
                }
                else
                {
                    // Regular statement - just evaluate it
                    var stmtProgram = Cons.FromEnumerable([JsSymbols.Program, stmt]);
                    lastValue = Evaluator.EvaluateProgram(stmtProgram, moduleEnv);
                }
            }
            else
            {
                // Regular statement - just evaluate it
                var stmtProgram = Cons.FromEnumerable([JsSymbols.Program, stmt]);
                lastValue = Evaluator.EvaluateProgram(stmtProgram, moduleEnv);
            }
            
            statements = statements.Rest;
        }
        
        return lastValue;
    }
    
    /// <summary>
    /// Processes an import statement and brings imported values into the module environment.
    /// </summary>
    private void EvaluateImport(Cons importCons, Environment moduleEnv)
    {
        // (import module-path) for side-effect imports
        // (import module-path default-import namespace-import named-imports) for regular imports
        var modulePath = importCons.Rest.Head as string;
        
        if (modulePath == null)
        {
            return; // Invalid import
        }
        
        // Load the module (for side effects)
        var exports = LoadModule(modulePath);
        
        // Check if there are any imports to handle
        if (importCons.Rest.Rest.IsEmpty)
        {
            return; // Side-effect only import
        }
        
        var defaultImport = importCons.Rest.Rest.Head as Symbol;
        var namespaceImport = !importCons.Rest.Rest.Rest.IsEmpty ? importCons.Rest.Rest.Rest.Head as Symbol : null;
        var namedImports = !importCons.Rest.Rest.Rest.IsEmpty && !importCons.Rest.Rest.Rest.Rest.IsEmpty 
            ? importCons.Rest.Rest.Rest.Rest.Head as Cons 
            : null;
        
        // Handle default import
        if (defaultImport != null && exports.TryGetValue("default", out var defaultValue))
        {
            moduleEnv.Define(defaultImport, defaultValue);
        }
        
        // Handle namespace import
        if (namespaceImport != null)
        {
            moduleEnv.Define(namespaceImport, exports);
        }
        
        // Handle named imports
        if (namedImports != null)
        {
            foreach (var importItem in namedImports)
            {
                if (importItem is Cons { Head: Symbol importHead } importItemCons &&
                    ReferenceEquals(importHead, JsSymbols.ImportNamed))
                {
                    var imported = importItemCons.Rest.Head as Symbol;
                    var local = importItemCons.Rest.Rest.Head as Symbol;
                    
                    if (imported != null && local != null)
                    {
                        if (exports.TryGetValue(imported.Name, out var value))
                        {
                            moduleEnv.Define(local, value);
                        }
                    }
                }
            }
        }
    }
}
