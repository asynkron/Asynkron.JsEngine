namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript generator object that implements the iterator protocol.
/// Generators are created by calling generator functions (function*) and can be
/// paused and resumed using yield expressions.
/// </summary>
internal sealed class JsGenerator : IJsCallable
{
    private readonly Cons _body;
    private readonly Cons _parameters;
    private readonly Environment _closure;
    private readonly IReadOnlyList<object?> _arguments;
    private object? _currentContinuation;
    private GeneratorState _state;
    private object? _yieldedValue;
    private bool _done;
    private Environment? _executionEnv;

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
    public JsGenerator(Cons parameters, Cons body, Environment closure, IReadOnlyList<object?> arguments)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _closure = closure ?? throw new ArgumentNullException(nameof(closure));
        _arguments = arguments ?? Array.Empty<object?>();
        _state = GeneratorState.Start;
        _done = false;
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
        {
            // Generator is already completed
            return CreateIteratorResult(null, true);
        }

        if (_state == GeneratorState.Completed)
        {
            _done = true;
            return CreateIteratorResult(null, true);
        }

        try
        {
            _state = GeneratorState.Executing;

            if (_currentContinuation == null)
            {
                // First call to next() - start executing the generator
                // For now, we'll execute the body directly without CPS transformation
                // This is a simplified implementation
                var result = ExecuteGeneratorBody(value);
                
                if (_yieldedValue != null)
                {
                    _state = GeneratorState.Suspended;
                    var yieldValue = _yieldedValue;
                    _yieldedValue = null;
                    return CreateIteratorResult(yieldValue, false);
                }
                else
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    return CreateIteratorResult(result, true);
                }
            }
            else
            {
                // Resume from suspension
                var result = ResumeSuspendedGenerator(value);
                
                if (_yieldedValue != null)
                {
                    _state = GeneratorState.Suspended;
                    var yieldValue = _yieldedValue;
                    _yieldedValue = null;
                    return CreateIteratorResult(yieldValue, false);
                }
                else
                {
                    _state = GeneratorState.Completed;
                    _done = true;
                    return CreateIteratorResult(result, true);
                }
            }
        }
        catch (ReturnSignal returnSignal)
        {
            _state = GeneratorState.Completed;
            _done = true;
            return CreateIteratorResult(returnSignal.Value, true);
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

    private object? ExecuteGeneratorBody(object? value)
    {
        // Create a new environment for this generator execution (first time only)
        if (_executionEnv == null)
        {
            _executionEnv = new Environment(_closure);
            
            // Bind parameters
            var (regularParams, restParam) = ParseParameterList(_parameters);
            
            // Bind regular parameters
            for (int i = 0; i < regularParams.Count; i++)
            {
                var paramValue = i < _arguments.Count ? _arguments[i] : null;
                _executionEnv.Define(regularParams[i], paramValue);
            }
            
            // Bind rest parameter if present
            if (restParam != null)
            {
                var restArgs = new JsArray();
                for (int i = regularParams.Count; i < _arguments.Count; i++)
                {
                    restArgs.Push(_arguments[i]);
                }
                _executionEnv.Define(restParam, restArgs);
            }
        }
        
        // Execute the body
        try
        {
            return Evaluator.EvaluateBlock(_body, _executionEnv);
        }
        catch (YieldSignal yieldSignal)
        {
            _yieldedValue = yieldSignal.Value;
            _currentContinuation = yieldSignal.Continuation;
            return null;
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
            {
                if (paramCons.Head is Symbol paramSymbol && paramSymbol.Name == "rest")
                {
                    // This is a rest parameter
                    if (paramCons.Rest.Head is Symbol restSymbol)
                    {
                        restParam = restSymbol;
                    }
                    break; // Rest param must be last
                }
            }
            
            // Regular parameter
            if (param is Symbol symbol)
            {
                regularParams.Add(symbol);
            }

            current = current.Rest;
        }

        return (regularParams, restParam);
    }

    private object? ResumeSuspendedGenerator(object? value)
    {
        // Resume execution from the continuation
        // This is a placeholder - in a full implementation, we'd use the stored continuation
        _state = GeneratorState.Completed;
        return null;
    }

    private static JsObject CreateIteratorResult(object? value, bool done)
    {
        var result = new JsObject();
        result.SetProperty("value", value);
        result.SetProperty("done", done);
        return result;
    }

    /// <summary>
    /// Internal method to handle yield expressions during evaluation.
    /// This is called by the evaluator when it encounters a yield expression.
    /// </summary>
    internal void Yield(object? value)
    {
        _yieldedValue = value;
        throw new YieldSignal(value, null);
    }
}

/// <summary>
/// Signal used internally to implement yield behavior.
/// Thrown when a yield expression is evaluated, caught by the generator.
/// </summary>
internal sealed class YieldSignal : Exception
{
    public object? Value { get; }
    public object? Continuation { get; }

    public YieldSignal(object? value, object? continuation)
    {
        Value = value;
        Continuation = continuation;
    }
}
