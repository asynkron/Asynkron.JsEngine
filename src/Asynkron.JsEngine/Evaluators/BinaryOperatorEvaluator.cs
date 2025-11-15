namespace Asynkron.JsEngine.Evaluators;

internal static class BinaryOperatorEvaluator
{
    internal static object Add(object? left, object? right)
    {
        // If either operand is a string, perform string concatenation
        if (left is string || right is string)
        {
            return ConversionHelpers.ToString(left) + ConversionHelpers.ToString(right);
        }

        // If either operand is an object or array, convert to string (ToPrimitive preference is string for +)
        if (left is JsObject || left is JsArray || right is JsObject || right is JsArray)
        {
            return ConversionHelpers.ToString(left) + ConversionHelpers.ToString(right);
        }

        // Handle BigInt + BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt + rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        // Otherwise, perform numeric addition
        return ConversionHelpers.ToNumber(left) + ConversionHelpers.ToNumber(right);
    }

    internal static object Subtract(object? left, object? right)
    {
        // Handle BigInt - BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt - rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return ConversionHelpers.ToNumber(left) - ConversionHelpers.ToNumber(right);
    }

    internal static object Multiply(object? left, object? right)
    {
        // Handle BigInt * BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt * rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return ConversionHelpers.ToNumber(left) * ConversionHelpers.ToNumber(right);
    }

    internal static object Power(object? left, object? right)
    {
        // Handle BigInt ** BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return JsBigInt.Pow(leftBigInt, rightBigInt);
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return Math.Pow(ConversionHelpers.ToNumber(left), ConversionHelpers.ToNumber(right));
    }

    internal static object Divide(object? left, object? right)
    {
        // Handle BigInt / BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt / rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return ConversionHelpers.ToNumber(left) / ConversionHelpers.ToNumber(right);
    }

    internal static object Modulo(object? left, object? right)
    {
        // Handle BigInt % BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt % rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        return ConversionHelpers.ToNumber(left) % ConversionHelpers.ToNumber(right);
    }

    internal static bool GreaterThan(object? left, object? right)
    {
        switch (left)
        {
            // Handle BigInt comparisons
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return leftBigInt > rightBigInt;
            // BigInt can be compared with Number in relational operators
            case JsBigInt lbi:
            {
                var rightNum = ConversionHelpers.ToNumber(right);
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return lbi.Value > new System.Numerics.BigInteger(rightNum);
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = ConversionHelpers.ToNumber(left);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return new System.Numerics.BigInteger(leftNum) > rbi.Value;
            }
            default:
                return ConversionHelpers.ToNumber(left) > ConversionHelpers.ToNumber(right);
        }
    }

    internal static bool GreaterThanOrEqual(object? left, object? right)
    {
        switch (left)
        {
            // Handle BigInt comparisons
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return leftBigInt >= rightBigInt;
            // BigInt can be compared with Number in relational operators
            case JsBigInt lbi:
            {
                var rightNum = ConversionHelpers.ToNumber(right);
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return lbi.Value >= new System.Numerics.BigInteger(rightNum);
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = ConversionHelpers.ToNumber(left);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return new System.Numerics.BigInteger(leftNum) >= rbi.Value;
            }
            default:
                return ConversionHelpers.ToNumber(left) >= ConversionHelpers.ToNumber(right);
        }
    }

    internal static bool LessThan(object? left, object? right)
    {
        switch (left)
        {
            // Handle BigInt comparisons
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return leftBigInt < rightBigInt;
            // BigInt can be compared with Number in relational operators
            case JsBigInt lbi:
            {
                var rightNum = ConversionHelpers.ToNumber(right);
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return lbi.Value < new System.Numerics.BigInteger(rightNum);
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = ConversionHelpers.ToNumber(left);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return new System.Numerics.BigInteger(leftNum) < rbi.Value;
            }
            default:
                return ConversionHelpers.ToNumber(left) < ConversionHelpers.ToNumber(right);
        }
    }

    internal static bool LessThanOrEqual(object? left, object? right)
    {
        switch (left)
        {
            // Handle BigInt comparisons
            case JsBigInt leftBigInt when right is JsBigInt rightBigInt:
                return leftBigInt <= rightBigInt;
            // BigInt can be compared with Number in relational operators
            case JsBigInt lbi:
            {
                var rightNum = ConversionHelpers.ToNumber(right);
                if (double.IsNaN(rightNum))
                {
                    return false;
                }

                return lbi.Value <= new System.Numerics.BigInteger(rightNum);
            }
        }

        switch (right)
        {
            case JsBigInt rbi:
            {
                var leftNum = ConversionHelpers.ToNumber(left);
                if (double.IsNaN(leftNum))
                {
                    return false;
                }

                return new System.Numerics.BigInteger(leftNum) <= rbi.Value;
            }
            default:
                return ConversionHelpers.ToNumber(left) <= ConversionHelpers.ToNumber(right);
        }
    }

    internal static bool StrictEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return left is not Double.NaN;
            // mirror JavaScript's NaN behaviour
        }

        if (left is null || right is null)
        {
            return false;
        }

        // BigInt strict equality
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt == rightBigInt;
        }

        // BigInt and Number are never strictly equal
        if ((left is JsBigInt && ConversionHelpers.IsNumeric(right)) || (ConversionHelpers.IsNumeric(left) && right is JsBigInt))
        {
            return false;
        }

        if (ConversionHelpers.IsNumeric(left) && ConversionHelpers.IsNumeric(right))
        {
            var leftNumber = ConversionHelpers.ToNumber(left);
            var rightNumber = ConversionHelpers.ToNumber(right);
            if (double.IsNaN(leftNumber) || double.IsNaN(rightNumber))
            {
                return false;
            }

            return leftNumber.Equals(rightNumber);
        }

        return left.GetType() == right.GetType() && Equals(left, right);
    }

    internal static bool LooseEquals(object? left, object? right)
    {
        while (true)
        {
            // JavaScript oddity: null == undefined (but null !== undefined)
            var leftIsNullish = left is null || (left is Symbol symL && ReferenceEquals(symL, JsSymbols.Undefined));
            var rightIsNullish = right is null || (right is Symbol symR && ReferenceEquals(symR, JsSymbols.Undefined));

            if (leftIsNullish && rightIsNullish)
            {
                return true;
            }

            if (leftIsNullish || rightIsNullish)
            {
                return false;
            }

            // If types are the same, use strict equality
            if (left?.GetType() == right?.GetType())
            {
                return StrictEquals(left, right);
            }

            // BigInt == Number: compare numerically (allowed in loose equality)
            if (left is JsBigInt leftBigInt && ConversionHelpers.IsNumeric(right))
            {
                var rightNum = ConversionHelpers.ToNumber(right);
                if (double.IsNaN(rightNum) || double.IsInfinity(rightNum))
                {
                    return false;
                }

                //TODO: Check for fractional part, how does this work in JS?
                // Check if right is an integer and compare
                if (rightNum == Math.Floor(rightNum))
                {
                    return leftBigInt.Value == new System.Numerics.BigInteger(rightNum);
                }

                return false;
            }

            if (ConversionHelpers.IsNumeric(left) && right is JsBigInt rightBigInt)
            {
                var leftNum = ConversionHelpers.ToNumber(left);
                if (double.IsNaN(leftNum) || double.IsInfinity(leftNum))
                {
                    return false;
                }

                // Check if left is an integer and compare
                //TODO: Check for fractional part, how does this work in JS?
                if (leftNum == Math.Floor(leftNum))
                {
                    return new System.Numerics.BigInteger(leftNum) == rightBigInt.Value;
                }

                return false;
            }

            // BigInt == String: convert string to BigInt if possible
            if (left is JsBigInt lbi && right is string str)
            {
                try
                {
                    var rightBigInt2 = new JsBigInt(str.Trim());
                    return lbi == rightBigInt2;
                }
                catch
                {
                    return false;
                }
            }

            if (left is string str2 && right is JsBigInt rbi)
            {
                try
                {
                    var leftBigInt2 = new JsBigInt(str2.Trim());
                    return leftBigInt2 == rbi;
                }
                catch
                {
                    return false;
                }
            }

            // Type coercion for loose equality
            // Number == String: convert string to number
            if (ConversionHelpers.IsNumeric(left) && right is string)
            {
                return ConversionHelpers.ToNumber(left).Equals(ConversionHelpers.ToNumber(right));
            }

            switch (left)
            {
                case string when ConversionHelpers.IsNumeric(right):
                    return ConversionHelpers.ToNumber(left).Equals(ConversionHelpers.ToNumber(right));
                // Boolean == anything: convert boolean to number
                case bool:
                    left = ConversionHelpers.ToNumber(left);
                    continue;
            }

            if (right is bool)
            {
                right = ConversionHelpers.ToNumber(right);
                continue;
            }

            // Object/Array == Primitive: convert object/array to primitive
            if (left is JsObject or JsArray && (ConversionHelpers.IsNumeric(right) || right is string))
            {
                // Try converting to primitive (via toString then toNumber if comparing to number)
                return ConversionHelpers.IsNumeric(right)
                    ? ConversionHelpers.ToNumber(left).Equals(ConversionHelpers.ToNumber(right))
                    : ConversionHelpers.ToString(left).Equals(right);
            }

            if (right is JsObject or JsArray && (ConversionHelpers.IsNumeric(left) || left is string))
            {
                // Try converting to primitive
                return ConversionHelpers.IsNumeric(left) ? ConversionHelpers.ToNumber(left).Equals(ConversionHelpers.ToNumber(right)) : left.Equals(ConversionHelpers.ToString(right));
            }

            // For other cases, use strict equality
            return StrictEquals(left, right);
            break;
        }
    }

    // Bitwise operations
    internal static object BitwiseAnd(object? left, object? right)
    {
        // Handle BigInt & BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt & rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ConversionHelpers.ToInt32(left);
        var rightInt = ConversionHelpers.ToInt32(right);
        return (double)(leftInt & rightInt);
    }

    internal static object BitwiseOr(object? left, object? right)
    {
        // Handle BigInt | BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt | rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ConversionHelpers.ToInt32(left);
        var rightInt = ConversionHelpers.ToInt32(right);
        return (double)(leftInt | rightInt);
    }

    internal static object BitwiseXor(object? left, object? right)
    {
        // Handle BigInt ^ BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return leftBigInt ^ rightBigInt;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ConversionHelpers.ToInt32(left);
        var rightInt = ConversionHelpers.ToInt32(right);
        return (double)(leftInt ^ rightInt);
    }

    internal static object BitwiseNot(object? operand)
    {
        // Handle ~BigInt
        if (operand is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        var operandInt = ConversionHelpers.ToInt32(operand);
        return (double)~operandInt;
    }

    internal static object LeftShift(object? left, object? right)
    {
        // Handle BigInt << BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            // BigInt shift requires int, so check range
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ConversionHelpers.ToInt32(left);
        var rightInt = ConversionHelpers.ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftInt << rightInt);
    }

    internal static object RightShift(object? left, object? right)
    {
        // Handle BigInt >> BigInt
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            // BigInt shift requires int, so check range
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw new InvalidOperationException("BigInt shift amount is too large");
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        // Cannot mix BigInt with Number
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("Cannot mix BigInt and other types, use explicit conversions");
        }

        var leftInt = ConversionHelpers.ToInt32(left);
        var rightInt = ConversionHelpers.ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftInt >> rightInt);
    }

    internal static object UnsignedRightShift(object? left, object? right)
    {
        // BigInt does not support >>> operator (unsigned right shift)
        if (left is JsBigInt || right is JsBigInt)
        {
            throw new InvalidOperationException("BigInts have no unsigned right shift, use >> instead");
        }

        var leftUInt = ConversionHelpers.ToUInt32(left);
        var rightInt = ConversionHelpers.ToInt32(right) & 0x1F; // Only use the bottom 5 bits
        return (double)(leftUInt >> rightInt);
    }

    internal static bool InOperator(object? left, object? right)
    {
        // Convert left operand to string (property name)
        var propertyName = left?.ToString() ?? "";

        // Right operand must be an object
        if (right is JsObject jsObj)
        {
            return jsObj.ContainsKey(propertyName);
        }

        // For non-objects, 'in' returns false
        return false;
    }

    internal static bool InstanceofOperator(object? left, object? right)
    {
        // Left operand must be an object
        if (left is not JsObject leftObj)
        {
            return false;
        }

        // Right operand must be a constructor function
        if (right is not IJsCallable)
        {
            return false;
        }

        // Get the prototype property from the constructor
        object? constructorPrototype = null;
        if (right is JsFunction jsFunc)
        {
            PropertyAccessEvaluator.TryGetPropertyValue(jsFunc, "prototype", out constructorPrototype);
        }
        else if (right is JsObject rightObj)
        {
            PropertyAccessEvaluator.TryGetPropertyValue(rightObj, "prototype", out constructorPrototype);
        }

        if (constructorPrototype is not JsObject prototypeObj)
        {
            return false;
        }

        // Walk up the prototype chain of the left operand
        var current = leftObj.Prototype;
        while (current != null)
        {
            if (ReferenceEquals(current, prototypeObj))
            {
                return true;
            }
            current = current.Prototype;
        }

        return false;
    }
}
