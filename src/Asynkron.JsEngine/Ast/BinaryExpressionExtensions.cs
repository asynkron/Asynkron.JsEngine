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

extension(BinaryExpression expression)
    {
        private object? EvaluateBinary(JsEnvironment environment,
            EvaluationContext context)
        {
            var left = EvaluateExpression(expression.Left, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            switch (expression.Operator)
            {
                case "&&":
                    return IsTruthy(left)
                        ? EvaluateExpression(expression.Right, environment, context)
                        : left;
                case "||":
                    return IsTruthy(left)
                        ? left
                        : EvaluateExpression(expression.Right, environment, context);
                case "??":
                    return IsNullish(left)
                        ? EvaluateExpression(expression.Right, environment, context)
                        : left;
            }

            var right = EvaluateExpression(expression.Right, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            return expression.Operator switch
            {
                "+" => Add(left, right, context),
                "-" => Subtract(left, right, context),
                "*" => Multiply(left, right, context),
                "/" => Divide(left, right, context),
                "%" => Modulo(left, right, context),
                "**" => Power(left, right, context),
                "==" => LooseEquals(left, right, context),
                "!=" => !LooseEquals(left, right, context),
                "===" => StrictEquals(left, right),
                "!==" => !StrictEquals(left, right),
                "<" => JsOps.LessThan(left, right, context),
                "<=" => JsOps.LessThanOrEqual(left, right, context),
                ">" => JsOps.GreaterThan(left, right, context),
                ">=" => JsOps.GreaterThanOrEqual(left, right, context),
                "&" => BitwiseAnd(left, right, context),
                "|" => BitwiseOr(left, right, context),
                "^" => BitwiseXor(left, right, context),
                "<<" => LeftShift(left, right, context),
                ">>" => RightShift(left, right, context),
                ">>>" => UnsignedRightShift(left, right, context),
                "in" => InOperator(left, right, context),
                "instanceof" => InstanceofOperator(left, right, context),
                _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
            };
        }
    }

}
