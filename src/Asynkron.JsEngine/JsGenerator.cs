namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript generator object that implements the iterator protocol.
/// Generators are created by calling generator functions (function*) and can be
/// paused and resumed using yield expressions.
/// 
/// This is a simplified implementation that works for sequential yields by re-executing
/// the generator body and skipping already-yielded values. For full generator support
/// with complex control flow, the full CPS transformation would be needed.
/// </summary>
public sealed class JsGenerator : IJsCallable
{
    private readonly Cons _body;
    private readonly Cons _parameters;
    private readonly JsEnvironment _closure;
    private readonly IReadOnlyList<object?> _arguments;
    private GeneratorState _state;
    private bool _done;
    private JsEnvironment? _executionEnv;
    private int _currentYieldIndex;

    private enum GeneratorState
    {
        Start,
        Suspended,
        Executing,
        Completed
    }

    /// <summary>
    /// Creates a new generator instance.
    /// </summary>
    /// <param name="parameters">The parameter list of the generator function</param>
    /// <param name="body">The body of the generator function</param>
    /// <param name="closure">The lexical environment where the generator was defined</param>
    /// <param name="arguments">The arguments passed when creating the generator</param>
    public JsGenerator(Cons parameters, Cons body, JsEnvironment closure, IReadOnlyList<object?> arguments)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _closure = closure ?? throw new ArgumentNullException(nameof(closure));
        _arguments = arguments ?? [];
        _state = GeneratorState.Start;
        _done = false;
        _currentYieldIndex = 0;
    }

    /// <summary>
    /// Implements the IJsCallable interface. Generators are called to instantiate them,
    /// but the actual iterator protocol methods (next, return, throw) are accessed as properties.
    /// </summary>
    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        // When a generator function is called, it returns itself (the generator object)
        // This allows the generator to be used as an iterator
        return this;
    }

    /// <summary>
    /// Implements the next() method of the iterator protocol.
    /// Resumes execution of the generator and returns an object with 'value' and 'done' properties.
    /// </summary>
    /// <param name="value">Optional value to send into the generator (becomes the result of yield)</param>
    /// <returns>An object with {value, done} properties</returns>
    public object? Next(object? value = null)
    {
        if (_done)
            // Generator is already completed
            return CreateIteratorResult(null, true);

        if (_state == GeneratorState.Completed)
        {
            _done = true;
            return CreateIteratorResult(null, true);
        }

        try
        {
            _state = GeneratorState.Executing;

            // Create execution environment if this is the first call
            if (_executionEnv == null)
            {
                _executionEnv = new JsEnvironment(_closure);
                BindParameters(_executionEnv);
            }

            // Set up yield tracking - this tells yields whether to actually yield or skip
            var yieldTracker = new YieldTracker(_currentYieldIndex);
            _executionEnv.Define(Symbol.Intern("__yieldTracker__"), yieldTracker);

            // Create context for this execution
            var context = new EvaluationContext();

            // Execute the body (or re-execute it to get to the next yield)
            var result = Evaluator.EvaluateBlock(_body, _executionEnv, context);

            // Check if yield was encountered
            if (context.IsYield)
            {
                _state = GeneratorState.Suspended;
                _currentYieldIndex++;
                return CreateIteratorResult(context.FlowValue, false);
            }

            // Check if return was encountered
            if (context.IsReturn)
            {
                _state = GeneratorState.Completed;
                _done = true;
                return CreateIteratorResult(context.FlowValue, true);
            }

            // If we get here without a yield, the generator is complete
            _state = GeneratorState.Completed;
            _done = true;
            return CreateIteratorResult(result, true);
        }
        catch (Exception)
        {
            _state = GeneratorState.Completed;
            _done = true;
            throw;
        }
    }

    /// <summary>
    /// Implements the return() method of the iterator protocol.
    /// Finishes the generator and returns the given value.
    /// </summary>
    public object? Return(object? value = null)
    {
        _state = GeneratorState.Completed;
        _done = true;
        return CreateIteratorResult(value, true);
    }

    /// <summary>
    /// Implements the throw() method of the iterator protocol.
    /// Throws an error inside the generator at the current suspension point.
    /// </summary>
    public object? Throw(object? error)
    {
        _state = GeneratorState.Completed;
        _done = true;
        throw new ThrowSignal(error);
    }

    private void BindParameters(JsEnvironment env)
    {
        var (regularParams, restParam) = ParseParameterList(_parameters);

        // Bind regular parameters
        for (var i = 0; i < regularParams.Count; i++)
        {
            var paramValue = i < _arguments.Count ? _arguments[i] : null;
            env.Define(regularParams[i], paramValue);
        }

        // Bind rest parameter if present
        if (restParam != null)
        {
            var restArgs = new JsArray();
            for (var i = regularParams.Count; i < _arguments.Count; i++) restArgs.Push(_arguments[i]);
            env.Define(restParam, restArgs);
        }
    }

    private static (List<Symbol> regularParams, Symbol? restParam) ParseParameterList(Cons parameters)
    {
        var regularParams = new List<Symbol>();
        Symbol? restParam = null;

        var current = parameters;
        while (!current.IsEmpty)
        {
            var param = current.Head;

            // Check if this is a rest parameter (wrapped in a rest cons)
            if (param is Cons paramCons && !paramCons.IsEmpty)
                if (paramCons.Head is Symbol paramSymbol && paramSymbol.Name == "rest")
                {
                    // This is a rest parameter
                    if (paramCons.Rest.Head is Symbol restSymbol) restParam = restSymbol;
                    break; // Rest param must be last
                }

            // Regular parameter
            if (param is Symbol symbol) regularParams.Add(symbol);

            current = current.Rest;
        }

        return (regularParams, restParam);
    }

    private static JsObject CreateIteratorResult(object? value, bool done)
    {
        var result = new JsObject();
        result.SetProperty("value", value);
        result.SetProperty("done", done);
        return result;
    }
}

/// <summary>
/// Tracks which yield has been reached during generator execution.
/// This is a simplified approach that works for sequential yields by re-executing
/// the function and skipping yields that have already been processed.
/// </summary>
public sealed class YieldTracker(int skipCount)
{
    private int _currentIndex = 0;

    public bool ShouldYield()
    {
        var should = _currentIndex >= skipCount;
        _currentIndex++;
        return should;
    }
}