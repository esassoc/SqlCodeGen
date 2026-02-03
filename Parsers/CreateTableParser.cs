using System.Text.RegularExpressions;
using SqlCodeGen.Models;

namespace SqlCodeGen.Parsers;

/// <summary>
/// Parses SQL CREATE TABLE statements to extract table and column definitions.
/// </summary>
public static class CreateTableParser
{
    // Pattern to match CREATE TABLE [schema].[TableName] or CREATE TABLE schema.TableName
    private static readonly Regex TableNamePattern = new(
        @"CREATE\s+TABLE\s+\[?(?<schema>\w+)\]?\.\[?(?<table>\w+)\]?\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to match a column definition line
    // Handles: [ColumnName] [type](length) NOT NULL CONSTRAINT ... PRIMARY KEY, IDENTITY(1,1)
    private static readonly Regex ColumnPattern = new(
        @"^\s*\[?(?<name>\w+)\]?\s+\[?(?<type>\w+)\]?(?:\s*\(\s*(?<length>\d+|max)\s*(?:,\s*\d+)?\s*\))?(?<notnull>\s+NOT\s+NULL)?(?<constraint>.*?)(?:,|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    // Pattern to detect IDENTITY
    private static readonly Regex IdentityPattern = new(
        @"IDENTITY\s*\(\s*\d+\s*,\s*\d+\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to detect inline PRIMARY KEY
    private static readonly Regex InlinePkPattern = new(
        @"PRIMARY\s+KEY",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to match standalone CONSTRAINT ... PRIMARY KEY (column)
    private static readonly Regex StandalonePkPattern = new(
        @"CONSTRAINT\s+\[?\w+\]?\s+PRIMARY\s+KEY\s*(?:CLUSTERED|NONCLUSTERED)?\s*\(\s*\[?(?<column>\w+)\]?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pattern to extract the referenced table from FK constraint
    // Handles: REFERENCES [dbo].[TableName](Column) or REFERENCES dbo.TableName(Column)
    private static readonly Regex ForeignKeyPattern = new(
        @"REFERENCES\s+\[?(?:\w+)\]?\.\[?(?<table>\w+)\]?\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses a SQL CREATE TABLE statement and returns a TableDefinition.
    /// </summary>
    /// <param name="sql">The SQL content of a CREATE TABLE file.</param>
    /// <returns>A TableDefinition if parsing succeeds, null otherwise.</returns>
    public static TableDefinition? Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        // Extract table name
        var tableMatch = TableNamePattern.Match(sql);
        if (!tableMatch.Success)
            return null;

        var schema = tableMatch.Groups["schema"].Value;
        var tableName = tableMatch.Groups["table"].Value;

        // Find the content between CREATE TABLE ( and the closing )
        var startIndex = tableMatch.Index + tableMatch.Length;
        var tableContent = ExtractTableContent(sql, startIndex);
        if (tableContent == null)
            return null;

        // Parse columns
        var columns = new List<ColumnDefinition>();
        string? inlinePrimaryKey = null;

        // Split by lines and process each potential column
        var lines = tableContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines and lines that are just constraints
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Skip standalone constraint definitions (start with CONSTRAINT but no column)
            if (trimmedLine.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                !ColumnPattern.IsMatch(trimmedLine))
            {
                // Check if this is a PRIMARY KEY constraint
                var pkMatch = StandalonePkPattern.Match(trimmedLine);
                if (pkMatch.Success)
                {
                    inlinePrimaryKey = pkMatch.Groups["column"].Value;
                }
                continue;
            }

            var columnMatch = ColumnPattern.Match(trimmedLine);
            if (!columnMatch.Success)
                continue;

            var columnName = columnMatch.Groups["name"].Value;
            var sqlType = columnMatch.Groups["type"].Value;
            var lengthStr = columnMatch.Groups["length"].Value;
            var isNotNull = columnMatch.Groups["notnull"].Success;
            var constraint = columnMatch.Groups["constraint"].Value;

            // Skip if this looks like a constraint line rather than a column definition
            // The column name or type could be a constraint keyword
            if (IsConstraintKeyword(columnName) || IsConstraintKeyword(sqlType))
                continue;

            // Parse length
            int? maxLength = null;
            if (!string.IsNullOrEmpty(lengthStr))
            {
                if (lengthStr.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    maxLength = -1; // Indicates MAX
                }
                else if (int.TryParse(lengthStr, out var len))
                {
                    maxLength = len;
                }
            }

            // Check for IDENTITY
            var isIdentity = IdentityPattern.IsMatch(constraint);

            // Check for inline PRIMARY KEY
            var isPrimaryKey = InlinePkPattern.IsMatch(constraint);
            if (isPrimaryKey)
            {
                inlinePrimaryKey = columnName;
            }

            // Check for FOREIGN KEY reference
            string? foreignKeyTable = null;
            var fkMatch = ForeignKeyPattern.Match(constraint);
            if (fkMatch.Success)
            {
                foreignKeyTable = fkMatch.Groups["table"].Value;
            }

            columns.Add(new ColumnDefinition(
                columnName,
                sqlType,
                isNullable: !isNotNull,
                maxLength,
                isIdentity,
                isPrimaryKey,
                foreignKeyTable));
        }

        if (columns.Count == 0)
            return null;

        // Also check for standalone PK constraint in the full SQL
        if (inlinePrimaryKey == null)
        {
            var standalonePkMatch = StandalonePkPattern.Match(sql);
            if (standalonePkMatch.Success)
            {
                inlinePrimaryKey = standalonePkMatch.Groups["column"].Value;
            }
        }

        return new TableDefinition(schema, tableName, columns, inlinePrimaryKey);
    }

    /// <summary>
    /// Extracts the table definition content between parentheses, handling nested parens.
    /// </summary>
    private static string? ExtractTableContent(string sql, int startIndex)
    {
        var depth = 1; // We've already passed the opening paren
        var endIndex = startIndex;

        while (endIndex < sql.Length && depth > 0)
        {
            var c = sql[endIndex];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            endIndex++;
        }

        if (depth != 0)
            return null;

        // Don't include the final closing paren
        return sql.Substring(startIndex, endIndex - startIndex - 1);
    }

    /// <summary>
    /// Checks if a word is a SQL constraint keyword rather than a type.
    /// </summary>
    private static bool IsConstraintKeyword(string word)
    {
        // Include ASC/DESC which appear in constraint definitions (sort order), not as column types
        var keywords = new[] { "CONSTRAINT", "PRIMARY", "FOREIGN", "UNIQUE", "CHECK", "DEFAULT", "INDEX", "REFERENCES", "ASC", "DESC" };
        return keywords.Contains(word, StringComparer.OrdinalIgnoreCase);
    }
}
