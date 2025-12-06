using System;

Console.WriteLine("Testing .NET Math.Floor with Infinity:");
Console.WriteLine($"Math.Floor(double.PositiveInfinity) = {Math.Floor(double.PositiveInfinity)}");
Console.WriteLine($"double.PositiveInfinity = {double.PositiveInfinity}");
Console.WriteLine($"Math.Floor(Infinity) == Infinity: {Math.Floor(double.PositiveInfinity) == double.PositiveInfinity}");
Console.WriteLine($"IsPositiveInfinity: {double.IsPositiveInfinity(Math.Floor(double.PositiveInfinity))}");
