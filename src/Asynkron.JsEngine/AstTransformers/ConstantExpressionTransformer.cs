using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.AstTransformers;

/// <summary>
/// Transforms S-expressions by folding constant expressions into their computed values.
/// For example, (+ 1 (* 2 7)) becomes 15.
/// This optimization runs before the CPS transformer to simplify the expression tree.
/// </summary>
public sealed class ConstantExpressionTransformer
{
    /// <summary>
    /// Transforms an S-expression program by folding constant expressions.
    /// </summary>
    /// <param name="program">The S-expression program to transform</param>
    /// <returns>The transformed S-expression program with constant expressions folded</returns>
    public Cons Transform(Cons program)
    {
        if (program == null || program.IsEmpty)
        {
            return program;
        }

        // Transform each element in the program
        return TransformCons(program);
    }

    /// <summary>
    /// Recursively transforms a Cons, folding constant expressions where possible.
    /// Sets Origin to track transformation even if no constant folding occurs.
    /// </summary>
    private Cons TransformCons(Cons cons)
    {
        if (cons.IsEmpty)
        {
            return cons;
        }

        var items = new List<object?>();
        var current = cons;
        var hasChanges = false;

        while (!current.IsEmpty)
        {
            var transformed = TransformExpression(current.Head);
            items.Add(transformed);

            // Check if anything changed
            if (!ReferenceEquals(transformed, current.Head))
            {
                hasChanges = true;
            }

            current = current.Rest;
        }

        // If nothing changed, return the original
        if (!hasChanges)
        {
            return cons;
        }

        var result = Cons.FromEnumerable(items);

        // Preserve source reference from the input cons
        if (cons.SourceReference != null)
        {
            result.WithSourceReference(cons.SourceReference);
        }

        // Set origin to track that this came from constant folding
        result.WithOrigin(cons);

        return result;
    }

    /// <summary>
    /// Transforms an expression, folding it if it's a constant expression.
    /// </summary>
    private object? TransformExpression(object? expr)
    {
        // If it's already a constant, return it
        if (expr.IsConstant())
        {
            return expr;
        }

        // If it's not a Cons, return as-is
        if (expr is not Cons cons || cons.IsEmpty)
        {
            return expr;
        }

        // Check if this is an operation that can be folded
        if (cons.Head is Symbol symbol)
        {
            // Try to fold the operation
            var folded = TryFoldOperation(symbol, cons);
            if (folded != null)
            {
                return folded;
            }
        }

        // Otherwise, recursively transform the Cons
        return TransformCons(cons);
    }

    /// <summary>
    /// Attempts to fold a constant operation into its result value.
    /// Returns null if the operation cannot be folded.
    /// </summary>
    private object? TryFoldOperation(Symbol operation, Cons cons)
    {
        // Get the operator name
        var opName = operation.Name;

        // Transform operands first
        var operands = new List<object?>();
        var current = cons.Rest;
        while (!current.IsEmpty)
        {
            operands.Add(TransformExpression(current.Head));
            current = current.Rest;
        }

        // Check if all operands are constants
        var allConstant = operands.All(op => op.IsConstant());
        if (!allConstant)
        {
            // Can't fold - rebuild cons with transformed operands
            // Check if any operands actually changed
            var operandsChanged = false;
            var origCurrent = cons.Rest;
            for (var i = 0; i < operands.Count && !origCurrent.IsEmpty; i++)
            {
                if (!ReferenceEquals(operands[i], origCurrent.Head))
                {
                    operandsChanged = true;
                    break;
                }

                origCurrent = origCurrent.Rest;
            }

            // If no operands changed, return the original
            if (!operandsChanged)
            {
                return null; // Signal to caller that no transformation occurred
            }

            // Operands changed, rebuild cons
            var items = new List<object?> { operation };
            items.AddRange(operands);
            var rebuilt = Cons.FromEnumerable(items);

            // Preserve SourceReference and set Origin
            if (cons.SourceReference != null)
            {
                rebuilt.WithSourceReference(cons.SourceReference);
            }

            rebuilt.WithOrigin(cons);

            return rebuilt;
        }

        // Try to fold based on operation type
        return opName switch
        {
            // Arithmetic operations
            "+" => FoldAddition(operands),
            "-" => FoldSubtraction(operands),
            "*" => FoldMultiplication(operands),
            "/" => FoldDivision(operands),
            "%" => FoldModulo(operands),
            "**" => FoldExponentiation(operands),

            // Unary operations
            "unary-" => FoldUnaryMinus(operands),
            "unary+" => FoldUnaryPlus(operands),
            "!" => FoldLogicalNot(operands),

            // Logical operations
            "&&" => FoldLogicalAnd(operands),
            "||" => FoldLogicalOr(operands),

            // Comparison operations
            "==" => FoldEquals(operands),
            "!=" => FoldNotEquals(operands),
            "===" => FoldStrictEquals(operands),
            "!==" => FoldStrictNotEquals(operands),
            "<" => FoldLessThan(operands),
            "<=" => FoldLessThanOrEqual(operands),
            ">" => FoldGreaterThan(operands),
            ">=" => FoldGreaterThanOrEqual(operands),

            // Bitwise operations
            "&" => FoldBitwiseAnd(operands),
            "|" => FoldBitwiseOr(operands),
            "^" => FoldBitwiseXor(operands),
            "~" => FoldBitwiseNot(operands),
            "<<" => FoldLeftShift(operands),
            ">>" => FoldRightShift(operands),
            ">>>" => FoldUnsignedRightShift(operands),

            _ => null // Can't fold this operation
        };
    }

    // Arithmetic operation folding

    private static object? FoldAddition(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var left = operands[0];
        var right = operands[1];

        // String concatenation
        if (left.IsConstantString() || right.IsConstantString())
        {
            var leftStr = CoerceToString(left);
            var rightStr = CoerceToString(right);
            return leftStr + rightStr;
        }

        // Numeric addition
        if (left.IsConstantNumber() && right.IsConstantNumber())
        {
            return (double)left! + (double)right!;
        }

        return null;
    }

    private static object? FoldSubtraction(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        return (double)operands[0]! - (double)operands[1]!;
    }

    private static object? FoldMultiplication(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        return (double)operands[0]! * (double)operands[1]!;
    }

    private static object? FoldDivision(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var divisor = (double)operands[1]!;
        if (divisor == 0)
        {
            var dividend = (double)operands[0]!;
            // JavaScript division by zero behavior
            if (dividend == 0)
            {
                return double.NaN;
            }

            if (dividend > 0)
            {
                return double.PositiveInfinity;
            }

            return double.NegativeInfinity;
        }

        return (double)operands[0]! / divisor;
    }

    private static object? FoldModulo(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var divisor = (double)operands[1]!;
        if (divisor == 0)
        {
            return double.NaN;
        }

        return (double)operands[0]! % divisor;
    }

    private static object? FoldExponentiation(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        return Math.Pow((double)operands[0]!, (double)operands[1]!);
    }

    // Unary operation folding

    private static object? FoldUnaryMinus(List<object?> operands)
    {
        if (operands.Count != 1)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber())
        {
            return null;
        }

        return -(double)operands[0]!;
    }

    private static object? FoldUnaryPlus(List<object?> operands)
    {
        if (operands.Count != 1)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber())
        {
            return null;
        }

        return (double)operands[0]!;
    }

    private static object? FoldLogicalNot(List<object?> operands)
    {
        if (operands.Count != 1)
        {
            return null;
        }

        var value = operands[0];
        return !CoerceToBoolean(value);
    }

    // Logical operation folding

    private static object? FoldLogicalAnd(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var left = operands[0];
        var right = operands[1];

        // JavaScript && returns the first falsy value or the last value
        if (!CoerceToBoolean(left))
        {
            return left;
        }

        return right;
    }

    private static object? FoldLogicalOr(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var left = operands[0];
        var right = operands[1];

        // JavaScript || returns the first truthy value or the last value
        if (CoerceToBoolean(left))
        {
            return left;
        }

        return right;
    }

    // Comparison operation folding

    private static object? FoldEquals(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        // Abstract equality (==) with type coercion
        return LooseEquals(operands[0], operands[1]);
    }

    private static object? FoldNotEquals(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        return !LooseEquals(operands[0], operands[1]);
    }

    private static object? FoldStrictEquals(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        return StrictEquals(operands[0], operands[1]);
    }

    private static object? FoldStrictNotEquals(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        return !StrictEquals(operands[0], operands[1]);
    }

    private static object? FoldLessThan(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var left = operands[0];
        var right = operands[1];

        // String comparison
        if (left.IsConstantString() && right.IsConstantString())
        {
            return string.CompareOrdinal((string)left!, (string)right!) < 0;
        }

        // Numeric comparison
        if (left.IsConstantNumber() && right.IsConstantNumber())
        {
            var leftNum = (double)left!;
            var rightNum = (double)right!;

            if (double.IsNaN(leftNum) || double.IsNaN(rightNum))
            {
                return false;
            }

            return leftNum < rightNum;
        }

        return null;
    }

    private static object? FoldLessThanOrEqual(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var left = operands[0];
        var right = operands[1];

        // String comparison
        if (left.IsConstantString() && right.IsConstantString())
        {
            return string.CompareOrdinal((string)left!, (string)right!) <= 0;
        }

        // Numeric comparison
        if (left.IsConstantNumber() && right.IsConstantNumber())
        {
            var leftNum = (double)left!;
            var rightNum = (double)right!;

            if (double.IsNaN(leftNum) || double.IsNaN(rightNum))
            {
                return false;
            }

            return leftNum <= rightNum;
        }

        return null;
    }

    private static object? FoldGreaterThan(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var left = operands[0];
        var right = operands[1];

        // String comparison
        if (left.IsConstantString() && right.IsConstantString())
        {
            return string.CompareOrdinal((string)left!, (string)right!) > 0;
        }

        // Numeric comparison
        if (left.IsConstantNumber() && right.IsConstantNumber())
        {
            var leftNum = (double)left!;
            var rightNum = (double)right!;

            if (double.IsNaN(leftNum) || double.IsNaN(rightNum))
            {
                return false;
            }

            return leftNum > rightNum;
        }

        return null;
    }

    private static object? FoldGreaterThanOrEqual(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        var left = operands[0];
        var right = operands[1];

        // String comparison
        if (left.IsConstantString() && right.IsConstantString())
        {
            return string.CompareOrdinal((string)left!, (string)right!) >= 0;
        }

        // Numeric comparison
        if (left.IsConstantNumber() && right.IsConstantNumber())
        {
            var leftNum = (double)left!;
            var rightNum = (double)right!;

            if (double.IsNaN(leftNum) || double.IsNaN(rightNum))
            {
                return false;
            }

            return leftNum >= rightNum;
        }

        return null;
    }

    // Bitwise operation folding

    private static object? FoldBitwiseAnd(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var left = ToInt32((double)operands[0]!);
        var right = ToInt32((double)operands[1]!);

        return (double)(left & right);
    }

    private static object? FoldBitwiseOr(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var left = ToInt32((double)operands[0]!);
        var right = ToInt32((double)operands[1]!);

        return (double)(left | right);
    }

    private static object? FoldBitwiseXor(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var left = ToInt32((double)operands[0]!);
        var right = ToInt32((double)operands[1]!);

        return (double)(left ^ right);
    }

    private static object? FoldBitwiseNot(List<object?> operands)
    {
        if (operands.Count != 1)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber())
        {
            return null;
        }

        var value = ToInt32((double)operands[0]!);

        return (double)~value;
    }

    private static object? FoldLeftShift(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var left = ToInt32((double)operands[0]!);
        var right = ToInt32((double)operands[1]!) & 0x1F; // Only use lower 5 bits

        return (double)(left << right);
    }

    private static object? FoldRightShift(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var left = ToInt32((double)operands[0]!);
        var right = ToInt32((double)operands[1]!) & 0x1F; // Only use lower 5 bits

        return (double)(left >> right);
    }

    private static object? FoldUnsignedRightShift(List<object?> operands)
    {
        if (operands.Count != 2)
        {
            return null;
        }

        if (!operands[0].IsConstantNumber() || !operands[1].IsConstantNumber())
        {
            return null;
        }

        var left = ToUint32((double)operands[0]!);
        var right = ToInt32((double)operands[1]!) & 0x1F; // Only use lower 5 bits

        return (double)(left >> right);
    }

    // Helper methods for type coercion

    private static string CoerceToString(object? value)
    {
        return value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IJsCallable => "function() { [native code] }",
            _ => value.ToString() ?? ""
        };
    }

    private static bool CoerceToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            double d => d != 0 && !double.IsNaN(d),
            string s => s.Length > 0,
            _ => true
        };
    }

    private static bool LooseEquals(object? left, object? right)
    {
        // Same type comparison
        if (left == null && right == null)
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        var leftType = left.GetType();
        var rightType = right.GetType();

        if (leftType == rightType)
        {
            return StrictEquals(left, right);
        }

        // Number and string comparison
        if (left.IsConstantNumber() && right.IsConstantString())
        {
            var rightNum = StringToNumber((string)right);
            return (double)left == rightNum;
        }

        if (left.IsConstantString() && right.IsConstantNumber())
        {
            var leftNum = StringToNumber((string)left);
            return leftNum == (double)right;
        }

        // Boolean to number comparison
        if (left.IsConstantBoolean() && right.IsConstantNumber())
        {
            var leftNum = (bool)left ? 1.0 : 0.0;
            return leftNum == (double)right;
        }

        if (left.IsConstantNumber() && right.IsConstantBoolean())
        {
            var rightNum = (bool)right ? 1.0 : 0.0;
            return (double)left == rightNum;
        }

        // Boolean to string comparison (convert boolean to number first, then compare)
        if (left.IsConstantBoolean() && right.IsConstantString())
        {
            var leftNum = (bool)left ? 1.0 : 0.0;
            var rightNum = StringToNumber((string)right);
            return leftNum == rightNum;
        }

        if (left.IsConstantString() && right.IsConstantBoolean())
        {
            var leftNum = StringToNumber((string)left);
            var rightNum = (bool)right ? 1.0 : 0.0;
            return leftNum == rightNum;
        }

        return false;
    }

    /// <summary>
    /// Converts a string to a number following JavaScript rules.
    /// </summary>
    private static double StringToNumber(string str)
    {
        // Empty string converts to 0
        if (string.IsNullOrEmpty(str))
        {
            return 0.0;
        }

        // Trim whitespace
        str = str.Trim();

        // After trimming, empty string converts to 0
        if (str.Length == 0)
        {
            return 0.0;
        }

        // Try to parse as a number
        if (double.TryParse(str, out var result))
        {
            return result;
        }

        // If parsing fails, return NaN
        return double.NaN;
    }

    private static bool StrictEquals(object? left, object? right)
    {
        if (left == null && right == null)
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        var leftType = left.GetType();
        var rightType = right.GetType();

        if (leftType != rightType)
        {
            return false;
        }

        if (left is double leftNum && right is double rightNum)
        {
            // NaN !== NaN
            if (double.IsNaN(leftNum) || double.IsNaN(rightNum))
            {
                return false;
            }

            return leftNum == rightNum;
        }

        return Equals(left, right);
    }

    private static int ToInt32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        // JavaScript ToInt32 conversion
        var int64 = (long)value;
        return (int)(int64 & 0xFFFFFFFF);
    }

    private static uint ToUint32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        // JavaScript ToUint32 conversion
        var int64 = (long)value;
        return (uint)(int64 & 0xFFFFFFFF);
    }
}