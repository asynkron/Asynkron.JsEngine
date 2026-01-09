using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        Console.WriteLine("=== Instruction Struct Size Audit ===\n");
        Console.WriteLine("This audit analyzes all instruction record types.");
        Console.WriteLine("Note: Instructions are C# records (reference types), not structs.");
        Console.WriteLine("This audit counts the number and types of fields in each instruction.\n");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        // Get all instruction types from the Instructions namespace
        var instructionTypes = typeof(Asynkron.JsEngine.Execution.Instructions.ExecutionInstruction)
            .Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Asynkron.JsEngine.Execution.Instructions" 
                && t.IsClass 
                && !t.IsAbstract 
                && t.Name.EndsWith("Instruction", StringComparison.Ordinal))
            .OrderBy(t => t.Name)
            .ToArray();

        var measurements = instructionTypes
            .Select(t => AnalyzeInstruction(t))
            .ToArray();

        // Display all measurements sorted by field count
        Console.WriteLine("Instruction Field Analysis (sorted by field count):");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"{"Instruction Type",-55} {"Fields",8} {"Est. Size (bytes)",18}");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        foreach (var measurement in measurements.OrderByDescending(m => m.EstimatedSize))
        {
            Console.WriteLine($"{measurement.Name,-55} {measurement.FieldCount,8} {measurement.EstimatedSize,18}");
        }

        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        // Display summary statistics
        var totalFields = measurements.Sum(m => m.FieldCount);
        var avgFields = measurements.Average(m => m.FieldCount);
        var minFields = measurements.Min(m => m.FieldCount);
        var maxFields = measurements.Max(m => m.FieldCount);
        var avgSize = measurements.Average(m => m.EstimatedSize);

        Console.WriteLine("Summary Statistics:");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"Total instruction types:  {measurements.Length}");
        Console.WriteLine($"Minimum field count:      {minFields}");
        Console.WriteLine($"Maximum field count:      {maxFields}");
        Console.WriteLine($"Average field count:      {avgFields:F2}");
        Console.WriteLine($"Average estimated size:   {avgSize:F2} bytes");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        // Identify largest instructions
        Console.WriteLine("Top 10 Largest Instructions (by estimated size):");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        foreach (var measurement in measurements.OrderByDescending(m => m.EstimatedSize).Take(10))
        {
            Console.WriteLine($"{measurement.Name,-55} {measurement.EstimatedSize,12} bytes ({measurement.FieldCount} fields)");
        }
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        // Detailed field analysis for largest instructions
        Console.WriteLine("Detailed Field Analysis for Top 10 Largest Instructions:");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        foreach (var measurement in measurements.OrderByDescending(m => m.EstimatedSize).Take(10))
        {
            Console.WriteLine($"\n{measurement.Name}:");
            foreach (var (fieldName, fieldType, fieldSize) in measurement.Fields)
            {
                Console.WriteLine($"  {fieldName,-40} {fieldType,-30} ~{fieldSize,4} bytes");
            }
        }
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        Console.WriteLine("\nOptimization Opportunities:");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("Fields that could potentially use smaller types:");
        var optimizableFields = measurements
            .SelectMany(m => m.Fields.Select(f => (m.Name, f.fieldName, f.fieldType, f.fieldSize)))
            .Where(f => f.fieldType == "System.Int32" || f.fieldType.StartsWith("System.Collections.Immutable", StringComparison.Ordinal))
            .OrderByDescending(f => f.fieldSize)
            .Take(20)
            .ToArray();

        foreach (var (instrName, fieldName, fieldType, fieldSize) in optimizableFields)
        {
            var suggestion = fieldType switch
            {
                "System.Int32" when fieldName.Contains("Index", StringComparison.OrdinalIgnoreCase) => "Consider short if < 32K",
                "System.Int32" when fieldName.Contains("Scope", StringComparison.OrdinalIgnoreCase) => "Consider short if < 32K",
                "System.Int32" when fieldName.Contains("Slot", StringComparison.OrdinalIgnoreCase) => "Consider short if < 32K",
                "System.Int32" => "Review range, consider short/byte",
                _ when fieldType.Contains("ImmutableArray", StringComparison.Ordinal) => "Large collection - verify necessity",
                _ when fieldType.Contains("ImmutableDictionary", StringComparison.Ordinal) => "Large collection - verify necessity",
                _ => ""
            };

            if (!string.IsNullOrEmpty(suggestion))
            {
                Console.WriteLine($"{instrName}.{fieldName,-30} ({fieldType,-40}): {suggestion}");
            }
        }
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n");

        Console.WriteLine("\nNext steps:");
        Console.WriteLine("1. Review profiling data to identify most frequently used instructions");
        Console.WriteLine("2. Prioritize optimization for large + frequently-used instructions");
        Console.WriteLine("3. Consider converting from reference-type records to struct records for small instructions");
        Console.WriteLine("4. Analyze if ImmutableArray/ImmutableDictionary fields are necessary or can be optimized");
        Console.WriteLine("5. Consider using smaller integer types (short, byte) where applicable");

        await Task.CompletedTask;
    }

    private static InstructionAnalysis AnalyzeInstruction(Type instructionType)
    {
        var properties = instructionType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields = new List<(string fieldName, string fieldType, int fieldSize)>();
        int totalSize = 8; // Object header on 64-bit

        foreach (var prop in properties)
        {
            var fieldType = prop.PropertyType;
            var fieldSize = EstimateFieldSize(fieldType);
            fields.Add((prop.Name, fieldType.FullName ?? fieldType.Name, fieldSize));
            totalSize += fieldSize;
        }

        return new InstructionAnalysis(
            instructionType.Name,
            properties.Length,
            totalSize,
            fields.ToArray()
        );
    }

    private static int EstimateFieldSize(Type type)
    {
        // Estimate field size based on type
        if (type == typeof(int) || type == typeof(uint)) return 4;
        if (type == typeof(long) || type == typeof(ulong)) return 8;
        if (type == typeof(short) || type == typeof(ushort)) return 2;
        if (type == typeof(byte) || type == typeof(sbyte)) return 1;
        if (type == typeof(bool)) return 1;
        if (type == typeof(char)) return 2;
        if (type == typeof(float)) return 4;
        if (type == typeof(double)) return 8;
        if (type.IsEnum) return EstimateFieldSize(Enum.GetUnderlyingType(type));
        if (type.IsValueType) return 16; // Conservative estimate for other structs
        
        // Reference types - pointer size (8 bytes on 64-bit)
        return 8;
    }

    private record InstructionAnalysis(
        string Name,
        int FieldCount,
        int EstimatedSize,
        (string fieldName, string fieldType, int fieldSize)[] Fields
    );
}
