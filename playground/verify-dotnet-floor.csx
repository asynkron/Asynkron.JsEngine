using System;

// Verify .NET Math.Floor behavior with Infinity
Console.WriteLine("Testing .NET Math.Floor with special values:");
Console.WriteLine($"Math.Floor(Infinity) = {Math.Floor(double.PositiveInfinity)}");
Console.WriteLine($"Math.Floor(-Infinity) = {Math.Floor(double.NegativeInfinity)}");
Console.WriteLine($"Math.Floor(NaN) = {Math.Floor(double.NaN)}");
Console.WriteLine($"Math.Floor(0.0) = {Math.Floor(0.0)}");
Console.WriteLine($"Math.Floor(-0.0) = {Math.Floor(-0.0)}");
Console.WriteLine($"Math.Floor(1.5) = {Math.Floor(1.5)}");
Console.WriteLine($"Math.Floor(-1.5) = {Math.Floor(-1.5)}");

Console.WriteLine("\nComparisons:");
Console.WriteLine($"Math.Floor(Infinity) == Infinity: {Math.Floor(double.PositiveInfinity) == double.PositiveInfinity}");
Console.WriteLine($"Math.Floor(Infinity) != Infinity: {Math.Floor(double.PositiveInfinity) != double.PositiveInfinity}");
