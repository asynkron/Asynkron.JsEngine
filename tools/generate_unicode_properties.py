#!/usr/bin/env python3
"""
Generate C# Unicode property escape data from Unicode Character Database (UCD) files.

Downloads UCD 16.0.0 data and generates a C# source file with character ranges
for all Unicode properties used in ECMAScript RegExp property escapes.

Usage:
    python3 tools/generate_unicode_properties.py

Output:
    src/Asynkron.JsEngine/StdLib/RegExp/UnicodePropertyData.generated.cs
"""

import os
import sys
import urllib.request
import re
from collections import defaultdict
from pathlib import Path

UNICODE_VERSION = "16.0.0"
BASE_URL = f"https://www.unicode.org/Public/{UNICODE_VERSION}/ucd"
EMOJI_URL = f"https://unicode.org/Public/{UNICODE_VERSION}/ucd/emoji/emoji-data.txt"

# Cache directory for downloaded files
CACHE_DIR = Path(__file__).parent / ".unicode-cache" / UNICODE_VERSION

# Output file
SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
OUTPUT_FILE = PROJECT_ROOT / "src" / "Asynkron.JsEngine" / "StdLib" / "RegExp" / "UnicodePropertyData.generated.cs"


def download_file(url, filename):
    """Download a UCD file, caching locally."""
    cache_path = CACHE_DIR / filename
    if cache_path.exists():
        return cache_path.read_text(encoding="utf-8")

    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    print(f"  Downloading {filename}...")
    try:
        with urllib.request.urlopen(url) as response:
            data = response.read().decode("utf-8")
        cache_path.write_text(data, encoding="utf-8")
        return data
    except Exception as e:
        print(f"  ERROR downloading {url}: {e}")
        sys.exit(1)


def parse_ranges(text, property_column=1):
    """Parse UCD file format: XXXX..YYYY ; PropertyName # comment"""
    props = defaultdict(list)
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        # Split on ; and take first two fields
        parts = line.split(";")
        if len(parts) < 2:
            continue
        range_part = parts[0].strip()
        prop_name = parts[property_column].strip().split("#")[0].strip()

        if ".." in range_part:
            start_s, end_s = range_part.split("..")
            start = int(start_s, 16)
            end = int(end_s, 16)
        else:
            start = int(range_part, 16)
            end = start

        props[prop_name].append((start, end))

    # Sort and merge ranges for each property
    for prop in props:
        props[prop] = merge_ranges(sorted(props[prop]))

    return props


def parse_unicode_data(text):
    """Parse UnicodeData.txt to get General_Category for each code point."""
    categories = defaultdict(list)
    prev_start = None

    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        fields = line.split(";")
        if len(fields) < 3:
            continue

        cp = int(fields[0], 16)
        name = fields[1]
        cat = fields[2]

        # Handle ranges like "<CJK Ideograph, First>" ... "<CJK Ideograph, Last>"
        if name.endswith(", First>"):
            prev_start = cp
            continue
        if name.endswith(", Last>"):
            if prev_start is not None:
                categories[cat].append((prev_start, cp))
                prev_start = None
            continue

        prev_start = None
        categories[cat].append((cp, cp))

    for cat in categories:
        categories[cat] = merge_ranges(sorted(categories[cat]))

    return categories


def parse_script_extensions(text):
    """Parse ScriptExtensions.txt which has space-separated script codes."""
    props = defaultdict(list)
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split(";")
        if len(parts) < 2:
            continue
        range_part = parts[0].strip()
        scripts_part = parts[1].strip().split("#")[0].strip()

        if ".." in range_part:
            start_s, end_s = range_part.split("..")
            start = int(start_s, 16)
            end = int(end_s, 16)
        else:
            start = int(range_part, 16)
            end = start

        # Each code point may have multiple script extensions
        for script_code in scripts_part.split():
            props[script_code].append((start, end))

    for prop in props:
        props[prop] = merge_ranges(sorted(props[prop]))

    return props


def merge_ranges(ranges):
    """Merge overlapping/adjacent ranges."""
    if not ranges:
        return []
    merged = [ranges[0]]
    for start, end in ranges[1:]:
        prev_start, prev_end = merged[-1]
        if start <= prev_end + 1:
            merged[-1] = (prev_start, max(prev_end, end))
        else:
            merged.append((start, end))
    return merged


def union_ranges(*range_lists):
    """Union multiple range lists."""
    all_ranges = []
    for rl in range_lists:
        all_ranges.extend(rl)
    return merge_ranges(sorted(all_ranges))


def complement_ranges(ranges, max_cp=0x10FFFF):
    """Compute complement of ranges within [0, max_cp]."""
    result = []
    prev = 0
    for start, end in ranges:
        if prev < start:
            result.append((prev, start - 1))
        prev = end + 1
    if prev <= max_cp:
        result.append((prev, max_cp))
    return result


# ECMAScript spec: mapping of property aliases to canonical names
# From Table 69: Non-binary Unicode property aliases and values
# https://tc39.es/ecma262/#table-nonbinary-unicode-properties

GC_LONG_TO_SHORT = {
    "Cased_Letter": "LC",
    "Close_Punctuation": "Pe",
    "Connector_Punctuation": "Pc",
    "Control": "Cc",
    "Currency_Symbol": "Sc",
    "Dash_Punctuation": "Pd",
    "Decimal_Number": "Nd",
    "Enclosing_Mark": "Me",
    "Final_Punctuation": "Pf",
    "Format": "Cf",
    "Initial_Punctuation": "Pi",
    "Letter": "L",
    "Letter_Number": "Nl",
    "Line_Separator": "Zl",
    "Lowercase_Letter": "Ll",
    "Mark": "M",
    "Math_Symbol": "Sm",
    "Modifier_Letter": "Lm",
    "Modifier_Symbol": "Sk",
    "Nonspacing_Mark": "Mn",
    "Number": "N",
    "Open_Punctuation": "Ps",
    "Other": "C",
    "Other_Letter": "Lo",
    "Other_Number": "No",
    "Other_Punctuation": "Po",
    "Other_Symbol": "So",
    "Paragraph_Separator": "Zp",
    "Private_Use": "Co",
    "Punctuation": "P",
    "Separator": "Z",
    "Space_Separator": "Zs",
    "Spacing_Mark": "Mc",
    "Surrogate": "Cs",
    "Symbol": "S",
    "Titlecase_Letter": "Lt",
    "Unassigned": "Cn",
    "Uppercase_Letter": "Lu",
}

# Short aliases used in ECMAScript
GC_SHORT_ALIASES = {
    "LC": "LC",  # Cased_Letter (composite)
    # All standard short names
    "Lu": "Lu", "Ll": "Ll", "Lt": "Lt", "Lm": "Lm", "Lo": "Lo", "L": "L",
    "Mn": "Mn", "Mc": "Mc", "Me": "Me", "M": "M",
    "Nd": "Nd", "Nl": "Nl", "No": "No", "N": "N",
    "Pc": "Pc", "Pd": "Pd", "Ps": "Ps", "Pe": "Pe", "Pi": "Pi", "Pf": "Pf", "Po": "Po", "P": "P",
    "Sm": "Sm", "Sc": "Sc", "Sk": "Sk", "So": "So", "S": "S",
    "Zs": "Zs", "Zl": "Zl", "Zp": "Zp", "Z": "Z",
    "Cc": "Cc", "Cf": "Cf", "Cs": "Cs", "Co": "Co", "Cn": "Cn", "C": "C",
}

# Script name aliases (short code -> long name used in UCD)
# From PropertyValueAliases.txt
SCRIPT_SHORT_TO_LONG = {}  # Will be populated from PropertyValueAliases.txt


def load_script_aliases(text):
    """Parse PropertyValueAliases.txt for script name mappings."""
    aliases = {}
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split(";")]
        if len(parts) < 3:
            continue
        if parts[0] != "sc":
            continue
        short_code = parts[1]
        long_name = parts[2]
        aliases[short_code] = long_name
        # Also map long name to itself
        aliases[long_name] = long_name
        # Handle additional aliases (e.g., Qaac for Coptic, Qaai for Inherited)
        for extra in parts[3:]:
            extra = extra.split("#")[0].strip()
            if extra:
                aliases[extra] = long_name
    return aliases


def format_ranges_cs(ranges, indent="            "):
    """Format ranges as C# array initializer."""
    if not ranges:
        return f"{indent}Array.Empty<(int, int)>()"

    lines = []
    items_per_line = 6
    for i in range(0, len(ranges), items_per_line):
        chunk = ranges[i:i + items_per_line]
        items = ", ".join(f"(0x{s:04X}, 0x{e:04X})" for s, e in chunk)
        lines.append(f"{indent}{items},")

    # Remove trailing comma from last line
    if lines:
        lines[-1] = lines[-1].rstrip(",")

    return "\n".join(lines)


def generate_cs_file(gc_data, binary_props, script_data, script_ext_data, script_aliases):
    """Generate the C# source file."""

    # Build composite General_Category ranges
    gc_composite = {}

    # Single categories
    for cat, ranges in gc_data.items():
        gc_composite[cat] = ranges

    # Composite categories
    gc_composite["LC"] = union_ranges(
        gc_data.get("Lu", []), gc_data.get("Ll", []), gc_data.get("Lt", []))
    gc_composite["L"] = union_ranges(
        gc_data.get("Lu", []), gc_data.get("Ll", []), gc_data.get("Lt", []),
        gc_data.get("Lm", []), gc_data.get("Lo", []))
    gc_composite["M"] = union_ranges(
        gc_data.get("Mn", []), gc_data.get("Mc", []), gc_data.get("Me", []))
    gc_composite["N"] = union_ranges(
        gc_data.get("Nd", []), gc_data.get("Nl", []), gc_data.get("No", []))
    gc_composite["P"] = union_ranges(
        gc_data.get("Pc", []), gc_data.get("Pd", []), gc_data.get("Ps", []),
        gc_data.get("Pe", []), gc_data.get("Pi", []), gc_data.get("Pf", []),
        gc_data.get("Po", []))
    gc_composite["S"] = union_ranges(
        gc_data.get("Sm", []), gc_data.get("Sc", []), gc_data.get("Sk", []),
        gc_data.get("So", []))
    gc_composite["Z"] = union_ranges(
        gc_data.get("Zs", []), gc_data.get("Zl", []), gc_data.get("Zp", []))
    gc_composite["C"] = union_ranges(
        gc_data.get("Cc", []), gc_data.get("Cf", []), gc_data.get("Cs", []),
        gc_data.get("Co", []), gc_data.get("Cn", []))

    # Build all entries: (lookup_key, canonical_name, ranges)
    entries = []

    # General_Category entries (short name lookups)
    for short, ranges in gc_composite.items():
        if ranges:
            entries.append(("gc", short, ranges))

    # General_Category long name aliases
    for long_name, short in GC_LONG_TO_SHORT.items():
        # The long name maps to the same ranges as the short name
        # We don't duplicate data - we'll use aliases in C#
        pass

    # Binary properties
    for prop_name, ranges in sorted(binary_props.items()):
        if ranges:
            entries.append(("binary", prop_name, ranges))

    # Script properties
    for script_name, ranges in sorted(script_data.items()):
        if ranges:
            entries.append(("sc", script_name, ranges))

    # Script_Extensions properties
    for script_name, ranges in sorted(script_ext_data.items()):
        if ranges:
            entries.append(("scx", script_name, ranges))

    # Now generate C# code
    lines = []
    lines.append("// <auto-generated>")
    lines.append(f"// Generated from Unicode Character Database v{UNICODE_VERSION}")
    lines.append("// Do not edit manually. Run: python3 tools/generate_unicode_properties.py")
    lines.append("// </auto-generated>")
    lines.append("")
    lines.append("#nullable enable")
    lines.append("")
    lines.append("using System.Collections.Frozen;")
    lines.append("")
    lines.append("namespace Asynkron.JsEngine.StdLib.RegExp;")
    lines.append("")
    lines.append("/// <summary>")
    lines.append(f"/// Unicode property escape data for ECMAScript RegExp (Unicode {UNICODE_VERSION}).")
    lines.append("/// Maps property names to code point ranges for \\p{{...}} and \\P{{...}} support.")
    lines.append("/// </summary>")
    lines.append("internal static class UnicodePropertyData")
    lines.append("{")

    # Generate General_Category alias dictionary
    lines.append("    /// <summary>")
    lines.append("    /// Maps General_Category long names and aliases to canonical short names.")
    lines.append("    /// </summary>")
    lines.append("    private static readonly FrozenDictionary<string, string> GeneralCategoryAliases =")
    lines.append("        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)")
    lines.append("        {")
    # Add long -> short mappings
    for long_name, short in sorted(GC_LONG_TO_SHORT.items()):
        lines.append(f'            ["{long_name}"] = "{short}",')
    # Add short -> short mappings (identity)
    for short in sorted(GC_SHORT_ALIASES.keys()):
        lines.append(f'            ["{short}"] = "{short}",')
    lines.append("        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);")
    lines.append("")

    # Generate Script alias dictionary
    lines.append("    /// <summary>")
    lines.append("    /// Maps script short codes to long names used as lookup keys.")
    lines.append("    /// </summary>")
    lines.append("    private static readonly FrozenDictionary<string, string> ScriptAliases =")
    lines.append("        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)")
    lines.append("        {")
    for short_code, long_name in sorted(script_aliases.items()):
        lines.append(f'            ["{short_code}"] = "{long_name}",')
    lines.append("        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);")
    lines.append("")

    # Generate range data - separate dictionaries for each property type
    for prop_type, type_label, dict_name in [
        ("gc", "General_Category", "GeneralCategoryRanges"),
        ("binary", "Binary property", "BinaryPropertyRanges"),
        ("sc", "Script", "ScriptRanges"),
        ("scx", "Script_Extensions", "ScriptExtensionRanges"),
    ]:
        type_entries = [(name, ranges) for t, name, ranges in entries if t == prop_type]
        if not type_entries:
            continue

        lines.append(f"    /// <summary>{type_label} code point ranges.</summary>")
        lines.append(f"    private static readonly FrozenDictionary<string, (int Start, int End)[]> {dict_name} =")
        lines.append("        new Dictionary<string, (int Start, int End)[]>(StringComparer.OrdinalIgnoreCase)")
        lines.append("        {")

        for name, ranges in sorted(type_entries):
            total = sum(e - s + 1 for s, e in ranges)
            lines.append(f'            ["{name}"] = // {len(ranges)} ranges, {total} code points')
            lines.append("            [")
            lines.append(format_ranges_cs(ranges, "                "))
            lines.append("            ],")

        lines.append(f"        }}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);")
        lines.append("")

    # Generate the lookup method
    lines.append("    /// <summary>")
    lines.append("    /// Resolves a Unicode property escape to code point ranges.")
    lines.append("    /// Supports: \\p{Lu}, \\p{General_Category=Lu}, \\p{gc=Lu},")
    lines.append("    /// \\p{Script=Latin}, \\p{sc=Latn}, \\p{Script_Extensions=Latin},")
    lines.append("    /// \\p{scx=Latn}, \\p{ASCII}, \\p{Alphabetic}, etc.")
    lines.append("    /// </summary>")
    lines.append("    /// <returns>Array of (Start, End) ranges, or null if property not found.</returns>")
    lines.append("    public static (int Start, int End)[]? Resolve(string propertyExpression)")
    lines.append("    {")
    lines.append("        // Check for key=value syntax: General_Category=Lu, gc=Lu, Script=Latin, etc.")
    lines.append("        var eqIndex = propertyExpression.IndexOf('=');")
    lines.append("        if (eqIndex >= 0)")
    lines.append("        {")
    lines.append("            var key = propertyExpression.AsSpan(0, eqIndex).Trim().ToString();")
    lines.append("            var value = propertyExpression.AsSpan(eqIndex + 1).Trim().ToString();")
    lines.append("")
    lines.append('            if (key is "General_Category" or "gc")')
    lines.append("            {")
    lines.append("                // Resolve alias to canonical short name")
    lines.append("                if (GeneralCategoryAliases.TryGetValue(value, out var canonical))")
    lines.append("                    value = canonical;")
    lines.append("                return GeneralCategoryRanges.GetValueOrDefault(value);")
    lines.append("            }")
    lines.append("")
    lines.append('            if (key is "Script" or "sc")')
    lines.append("            {")
    lines.append("                // Resolve script alias")
    lines.append("                if (ScriptAliases.TryGetValue(value, out var canonical))")
    lines.append("                    value = canonical;")
    lines.append("                return ScriptRanges.GetValueOrDefault(value);")
    lines.append("            }")
    lines.append("")
    lines.append('            if (key is "Script_Extensions" or "scx")')
    lines.append("            {")
    lines.append("                // Resolve script alias")
    lines.append("                if (ScriptAliases.TryGetValue(value, out var canonical))")
    lines.append("                    value = canonical;")
    lines.append("                return ScriptExtensionRanges.GetValueOrDefault(value);")
    lines.append("            }")
    lines.append("")
    lines.append("            return null; // Unknown property key")
    lines.append("        }")
    lines.append("")
    lines.append("        // No key=value syntax. Try in order:")
    lines.append("        // 1. General_Category (short or long name)")
    lines.append("        if (GeneralCategoryAliases.TryGetValue(propertyExpression, out var gcCanonical))")
    lines.append("        {")
    lines.append("            return GeneralCategoryRanges.GetValueOrDefault(gcCanonical);")
    lines.append("        }")
    lines.append("")
    lines.append("        // 2. Binary property")
    lines.append("        if (BinaryPropertyRanges.TryGetValue(propertyExpression, out var binaryRanges))")
    lines.append("        {")
    lines.append("            return binaryRanges;")
    lines.append("        }")
    lines.append("")
    lines.append("        // 3. Script (long name)")
    lines.append("        if (ScriptRanges.TryGetValue(propertyExpression, out var scriptRanges))")
    lines.append("        {")
    lines.append("            return scriptRanges;")
    lines.append("        }")
    lines.append("")
    lines.append("        return null; // Unknown property")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    return "\n".join(lines)


def main():
    print(f"Generating Unicode property data from UCD {UNICODE_VERSION}...")

    # Download UCD files
    print("Downloading UCD files...")
    unicode_data_text = download_file(f"{BASE_URL}/UnicodeData.txt", "UnicodeData.txt")
    prop_list_text = download_file(f"{BASE_URL}/PropList.txt", "PropList.txt")
    derived_core_text = download_file(f"{BASE_URL}/DerivedCoreProperties.txt", "DerivedCoreProperties.txt")
    derived_binary_text = download_file(f"{BASE_URL}/extracted/DerivedBinaryProperties.txt", "DerivedBinaryProperties.txt")
    derived_normalization_text = download_file(f"{BASE_URL}/DerivedNormalizationProps.txt", "DerivedNormalizationProps.txt")
    scripts_text = download_file(f"{BASE_URL}/Scripts.txt", "Scripts.txt")
    script_ext_text = download_file(f"{BASE_URL}/ScriptExtensions.txt", "ScriptExtensions.txt")
    emoji_text = download_file(EMOJI_URL, "emoji-data.txt")
    pva_text = download_file(f"{BASE_URL}/PropertyValueAliases.txt", "PropertyValueAliases.txt")

    # Parse files
    print("Parsing UCD data...")
    gc_data = parse_unicode_data(unicode_data_text)
    prop_list = parse_ranges(prop_list_text)
    derived_core = parse_ranges(derived_core_text)
    derived_binary = parse_ranges(derived_binary_text)
    derived_normalization = parse_ranges(derived_normalization_text)
    script_data = parse_ranges(scripts_text)
    script_ext_raw = parse_script_extensions(script_ext_text)
    emoji_data = parse_ranges(emoji_text)
    script_aliases = load_script_aliases(pva_text)

    # Merge all binary properties
    binary_props = {}
    for name, ranges in prop_list.items():
        binary_props[name] = ranges
    for name, ranges in derived_core.items():
        binary_props[name] = ranges
    for name, ranges in derived_binary.items():
        binary_props[name] = ranges
    for name, ranges in derived_normalization.items():
        binary_props[name] = ranges
    for name, ranges in emoji_data.items():
        binary_props[name] = ranges

    # Add the special "Any" and "Assigned" properties
    binary_props["Any"] = [(0x0000, 0x10FFFF)]

    # UnicodeData.txt lists assigned code points only; Cn gaps are implicit.
    # Synthesize Cn from the complement of the listed categories so that the
    # General_Category=C and binary Assigned properties both include unassigned
    # code points correctly.
    assigned_ranges = merge_ranges(sorted(rng for ranges in gc_data.values() for rng in ranges))
    gc_data["Cn"] = complement_ranges(assigned_ranges)
    binary_props["Assigned"] = assigned_ranges

    # "ASCII" property
    binary_props["ASCII"] = [(0x0000, 0x007F)]

    # Download PropertyAliases.txt for binary property short names
    prop_aliases_text = download_file(f"{BASE_URL}/PropertyAliases.txt", "PropertyAliases.txt")

    # Build binary property aliases (short name → long name)
    # e.g., AHex → ASCII_Hex_Digit, Alpha → Alphabetic, space → White_Space
    for line in prop_aliases_text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split(";")]
        if len(parts) < 2:
            continue
        long_name = parts[1]
        if long_name not in binary_props:
            continue
        # Map all aliases (short name + any additional aliases) to the canonical property
        for alias_part in [parts[0]] + parts[2:]:
            alias = alias_part.split("#")[0].strip()
            if alias and alias not in binary_props:
                binary_props[alias] = binary_props[long_name]

    # Also add General_Category value aliases from PropertyValueAliases.txt
    # e.g., gc ; Lu ; Uppercase_Letter → ensure both Lu and Uppercase_Letter work
    for line in pva_text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split(";")]
        if len(parts) < 3:
            continue
        if parts[0] == "gc":
            short_alias = parts[1]
            long_name = parts[2]
            # Add any additional aliases beyond what's already in our mapping
            # (some GC values have 3+ aliases)
            for alias in parts[1:]:
                # Strip inline comments (e.g. "Combining_Mark  # Mc | Me | Mn")
                alias = alias.split("#")[0].strip()
                if alias and alias not in GC_LONG_TO_SHORT and alias not in GC_SHORT_ALIASES:
                    # Map to the canonical short name
                    if short_alias in GC_SHORT_ALIASES:
                        GC_LONG_TO_SHORT[alias] = GC_SHORT_ALIASES[short_alias]
                    elif short_alias in GC_LONG_TO_SHORT:
                        GC_LONG_TO_SHORT[alias] = GC_LONG_TO_SHORT[short_alias]

    # For Script_Extensions, each code point that has NO scx entry
    # inherits its Script value. We need to merge.
    # First, build the full Script_Extensions data
    script_ext_full = defaultdict(list)

    # Start with script data for each script
    for script_name, ranges in script_data.items():
        script_ext_full[script_name].extend(ranges)

    # Override with Script_Extensions data
    # For code points in ScriptExtensions.txt, they belong to MULTIPLE scripts
    # We need to add them to each script's extension list
    for script_code, ranges in script_ext_raw.items():
        # Resolve short code to long name
        long_name = script_aliases.get(script_code, script_code)
        script_ext_full[long_name].extend(ranges)

    # Merge ranges
    for script_name in script_ext_full:
        script_ext_full[script_name] = merge_ranges(sorted(script_ext_full[script_name]))

    # Generate C# file
    print("Generating C# source...")
    cs_code = generate_cs_file(gc_data, binary_props, script_data, dict(script_ext_full), script_aliases)

    # Write output
    OUTPUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_FILE.write_text(cs_code, encoding="utf-8")
    print(f"Written to: {OUTPUT_FILE}")

    # Statistics
    total_entries = len(gc_data) + len(binary_props) + len(script_data) + len(script_ext_full)
    print(f"Total properties: {total_entries}")
    print(f"  General_Category: {len(gc_data)}")
    print(f"  Binary: {len(binary_props)}")
    print(f"  Script: {len(script_data)}")
    print(f"  Script_Extensions: {len(script_ext_full)}")
    print(f"File size: {OUTPUT_FILE.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
