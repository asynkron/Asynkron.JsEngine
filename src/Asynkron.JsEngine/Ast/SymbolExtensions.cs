using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(Symbol? key)
    {
        private DelegatedYieldState? GetDelegatedState(JsEnvironment environment)
        {
            if (key is null)
            {
                return null;
            }

            if (environment.TryGet(key, out var existing) && existing is DelegatedYieldState state)
            {
                return state;
            }

            return null;
        }
    }

extension(Symbol? key)
    {
        private void StoreDelegatedState(JsEnvironment environment, DelegatedYieldState state)
        {
            if (key is null)
            {
                return;
            }

            if (environment.TryGet(key, out _))
            {
                environment.Assign(key, state);
            }
            else
            {
                environment.Define(key, state);
            }
        }
    }

extension(Symbol? key)
    {
        private void ClearDelegatedState(JsEnvironment environment)
        {
            if (key is null)
            {
                return;
            }

            if (environment.TryGet(key, out _))
            {
                environment.Assign(key, null);
            }
        }
    }

}
