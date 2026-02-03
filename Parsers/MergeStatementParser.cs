using System.Text.RegularExpressions;
using SqlCodeGen.Models;

namespace SqlCodeGen.Parsers;

/// <summary>
/// Parses SQL MERGE statements to extract lookup table data.
/// </summary>
public static class MergeStatementParser
{
    // Pattern to match MERGE INTO [schema].[TableName] or MERGE INTO schema.TableName
    private static readonly Regex MergeTablePattern = new(
        @"MERGE\s+INTO\s+\[?(?<schema>\w+)\]?\.\[?(?<table>\w+)\]?\s+AS\s+Target",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to extract column names from AS Source (col1, col2, ...)
    private static readonly Regex SourceColumnsPattern = new(
        @"AS\s+Source\s*\(\s*(?<columns>[^)]+)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to find the VALUES section
    private static readonly Regex ValuesStartPattern = new(
        @"USING\s*\(\s*VALUES",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses a SQL MERGE statement and returns LookupTableData.
    /// </summary>
    /// <param name="sql">The SQL content of a MERGE file.</param>
    /// <returns>A LookupTableData if parsing succeeds, null otherwise.</returns>
    public static LookupTableData? Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        // Extract table name
        var tableMatch = MergeTablePattern.Match(sql);
        if (!tableMatch.Success)
            return null;

        var schema = tableMatch.Groups["schema"].Value;
        var tableName = tableMatch.Groups["table"].Value;

        // Extract column names
        var columnsMatch = SourceColumnsPattern.Match(sql);
        if (!columnsMatch.Success)
            return null;

        var columnsStr = columnsMatch.Groups["columns"].Value;
        var columnNames = columnsStr
            .Split(',')
            .Select(c => c.Trim().Trim('[', ']'))
            .ToList();

        // Find the VALUES section
        var valuesMatch = ValuesStartPattern.Match(sql);
        if (!valuesMatch.Success)
            return null;

        // Extract the VALUES content (between USING (VALUES and AS Source)
        var valuesStartIndex = valuesMatch.Index + valuesMatch.Length;
        var asSourceIndex = sql.IndexOf("AS Source", valuesStartIndex, StringComparison.OrdinalIgnoreCase);
        if (asSourceIndex < 0)
            return null;

        var valuesContent = sql.Substring(valuesStartIndex, asSourceIndex - valuesStartIndex).Trim();

        // Parse each row of values
        var rows = ParseValueRows(valuesContent);
        if (rows.Count == 0)
            return null;

        return new LookupTableData(schema, tableName, columnNames, rows);
    }

    /// <summary>
    /// Parses the VALUES content to extract individual rows.
    /// Values look like: (1, 'Name', 'Display Name'), (2, 'Name2', 'Display Name2')
    /// </summary>
    private static List<LookupTableRow> ParseValueRows(string valuesContent)
    {
        var rows = new List<LookupTableRow>();

        // Find each row by matching balanced parentheses
        var i = 0;
        while (i < valuesContent.Length)
        {
            // Find the start of a row
            while (i < valuesContent.Length && valuesContent[i] != '(')
                i++;

            if (i >= valuesContent.Length)
                break;

            // Extract the row content
            var rowStart = i + 1;
            var depth = 1;
            i++;

            while (i < valuesContent.Length && depth > 0)
            {
                var c = valuesContent[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == '\'' && depth == 1)
                {
                    // Skip string content
                    i++;
                    while (i < valuesContent.Length)
                    {
                        if (valuesContent[i] == '\'')
                        {
                            // Check for escaped quote ('')
                            if (i + 1 < valuesContent.Length && valuesContent[i + 1] == '\'')
                            {
                                i += 2;
                                continue;
                            }
                            break;
                        }
                        i++;
                    }
                }
                i++;
            }

            if (depth == 0)
            {
                var rowContent = valuesContent.Substring(rowStart, i - rowStart - 1);
                var values = ParseRowValues(rowContent);
                if (values.Count > 0)
                {
                    rows.Add(new LookupTableRow(values));
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Parses a single row's values, handling quoted strings properly.
    /// </summary>
    private static List<string> ParseRowValues(string rowContent)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inString = false;
        var i = 0;

        while (i < rowContent.Length)
        {
            var c = rowContent[i];

            if (inString)
            {
                if (c == '\'')
                {
                    // Check for escaped quote ('')
                    if (i + 1 < rowContent.Length && rowContent[i + 1] == '\'')
                    {
                        current.Append('\'');
                        i += 2;
                        continue;
                    }
                    inString = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '\'')
                {
                    inString = true;
                }
                else if (c == 'N' && i + 1 < rowContent.Length && rowContent[i + 1] == '\'')
                {
                    // SQL Unicode string literal N'string' - skip the N prefix
                    i++;
                    inString = true;
                }
                else if (c == ',')
                {
                    values.Add(current.ToString().Trim());
                    current.Clear();
                }
                else if (!char.IsWhiteSpace(c) || current.Length > 0)
                {
                    // Only add non-whitespace, or whitespace if we've started a value
                    if (!char.IsWhiteSpace(c))
                        current.Append(c);
                }
            }
            i++;
        }

        // Add the last value
        var lastValue = current.ToString().Trim();
        values.Add(lastValue);

        return values;
    }
}
