using System.Text;
using System.Text.RegularExpressions;
using SqlCodeGen.Models;

namespace SqlCodeGen.Generators;

/// <summary>
/// Generates TypeScript enum files for lookup tables.
/// </summary>
public static class TypeScriptEnumGenerator
{
    /// <summary>
    /// Generates TypeScript content for a lookup table.
    /// </summary>
    public static string Generate(TableDefinition table, LookupTableData lookupData)
    {
        var tableName = table.TableName;
        var pkColumn = table.PrimaryKeyColumn ?? $"{tableName}ID";

        // Find columns
        var idColIndex = lookupData.GetColumnIndex(pkColumn);
        if (idColIndex < 0) idColIndex = lookupData.GetColumnIndexBySuffix("ID");

        // Find the system name column (NOT DisplayName)
        var nameColIndex = FindSystemNameColumnIndex(lookupData, tableName);
        var displayNameColIndex = FindDisplayNameColumnIndex(lookupData, tableName);

        // Find SortOrder column if it exists
        var sortOrderColIndex = lookupData.GetColumnIndex("SortOrder");
        if (sortOrderColIndex < 0) sortOrderColIndex = lookupData.GetColumnIndexBySuffix("SortOrder");

        // If no DisplayName, use Name
        if (displayNameColIndex < 0)
        {
            displayNameColIndex = nameColIndex;
        }

        var sb = new StringBuilder();
        sb.AppendLine("//  IMPORTANT:");
        sb.AppendLine("//  This file is generated. Your changes will be lost.");
        sb.AppendLine($"//  Source Table: [{table.Schema}].[{table.TableName}]");
        sb.AppendLine();
        sb.AppendLine("import { LookupTableEntry } from \"src/app/shared/models/lookup-table-entry\";");
        sb.AppendLine("import { SelectDropdownOption } from \"src/app/shared/components/forms/form-field/form-field.component\"");
        sb.AppendLine();

        // Enum
        sb.AppendLine($"export enum {tableName}Enum {{");
        foreach (var row in lookupData.Rows)
        {
            var id = row.GetValue(idColIndex) ?? "0";
            var name = SanitizeIdentifier(row.GetValue(nameColIndex) ?? "Unknown");
            sb.AppendLine($"  {name} = {id},");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // LookupTableEntry array
        var pluralName = GetPluralName(tableName);
        sb.AppendLine($"export const {pluralName}: LookupTableEntry[]  = [");

        var sortOrder = 10;
        foreach (var row in lookupData.Rows)
        {
            var id = row.GetValue(idColIndex) ?? "0";
            var rawName = row.GetValue(nameColIndex) ?? "Unknown";
            var name = SanitizeIdentifier(rawName);
            var displayName = row.GetValue(displayNameColIndex) ?? rawName;

            // Use explicit SortOrder if column exists, otherwise calculate from ID
            int actualSortOrder;
            if (sortOrderColIndex >= 0)
            {
                actualSortOrder = row.GetIntValue(sortOrderColIndex) ?? sortOrder;
            }
            else
            {
                // Default: ID * 10
                actualSortOrder = (row.GetIntValue(idColIndex) ?? 1) * 10;
            }

            sb.AppendLine($"  {{ Name: \"{name}\", DisplayName: \"{EscapeJs(displayName)}\", Value: {id}, SortOrder: {actualSortOrder} }},");
            sortOrder += 10;
        }
        sb.AppendLine("];");

        // SelectDropdownOption array
        sb.AppendLine($"export const {pluralName}AsSelectDropdownOptions = {pluralName}.map((x) => ({{ Value: x.Value, Label: x.DisplayName, SortOrder: x.SortOrder }} as SelectDropdownOption));");

        return sb.ToString();
    }

    /// <summary>
    /// Converts a table name to the TypeScript file name format (kebab-case with -enum suffix).
    /// Examples: BenefitCategory -> benefit-category-enum.ts
    ///           BMPRegistrationSection -> b-m-p-registration-section-enum.ts
    /// </summary>
    public static string GetTypeScriptFileName(string tableName)
    {
        return ToKebabCase(tableName) + "-enum.ts";
    }

    /// <summary>
    /// Converts PascalCase to kebab-case, treating consecutive uppercase as individual letters.
    /// </summary>
    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new StringBuilder();

        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    result.Append('-');
                }
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Gets the plural form of a table name for the array variable name.
    /// </summary>
    private static string GetPluralName(string tableName)
    {
        // Simple pluralization rules
        if (tableName.EndsWith("y", StringComparison.Ordinal) &&
            !tableName.EndsWith("ay", StringComparison.Ordinal) &&
            !tableName.EndsWith("ey", StringComparison.Ordinal) &&
            !tableName.EndsWith("oy", StringComparison.Ordinal) &&
            !tableName.EndsWith("uy", StringComparison.Ordinal))
        {
            return tableName.Substring(0, tableName.Length - 1) + "ies";
        }
        if (tableName.EndsWith("s", StringComparison.Ordinal) ||
            tableName.EndsWith("x", StringComparison.Ordinal) ||
            tableName.EndsWith("ch", StringComparison.Ordinal) ||
            tableName.EndsWith("sh", StringComparison.Ordinal))
        {
            return tableName + "es";
        }
        return tableName + "s";
    }

    private static string EscapeJs(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Sanitizes a string value to be a valid TypeScript identifier.
    /// </summary>
    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();
        if (result.Length == 0)
            return "Unknown";

        // Ensure it starts with a letter or underscore (not a digit)
        if (char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        return result;
    }

    /// <summary>
    /// Finds the system name column index (e.g., BenefitCategoryName, not BenefitCategoryDisplayName).
    /// </summary>
    private static int FindSystemNameColumnIndex(LookupTableData lookupData, string tableName)
    {
        // First, try exact match: {TableName}Name
        var exactName = $"{tableName}Name";
        for (int i = 0; i < lookupData.ColumnNames.Count; i++)
        {
            if (lookupData.ColumnNames[i].Equals(exactName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // Then, look for any column ending with "Name" but NOT "DisplayName"
        for (int i = 0; i < lookupData.ColumnNames.Count; i++)
        {
            var col = lookupData.ColumnNames[i];
            if (col.EndsWith("Name", StringComparison.OrdinalIgnoreCase) &&
                !col.EndsWith("DisplayName", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // Fallback: any Name column
        return lookupData.GetColumnIndexBySuffix("Name");
    }

    /// <summary>
    /// Finds the display name column index.
    /// </summary>
    private static int FindDisplayNameColumnIndex(LookupTableData lookupData, string tableName)
    {
        // First, try exact match: {TableName}DisplayName
        var exactName = $"{tableName}DisplayName";
        for (int i = 0; i < lookupData.ColumnNames.Count; i++)
        {
            if (lookupData.ColumnNames[i].Equals(exactName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // Then, look for any column ending with "DisplayName"
        for (int i = 0; i < lookupData.ColumnNames.Count; i++)
        {
            if (lookupData.ColumnNames[i].EndsWith("DisplayName", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
