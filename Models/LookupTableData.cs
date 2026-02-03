namespace SqlCodeGen.Models;

/// <summary>
/// Represents the data from a MERGE statement for a lookup table.
/// </summary>
public sealed class LookupTableData : IEquatable<LookupTableData>
{
    public string Schema { get; }
    public string TableName { get; }
    public IReadOnlyList<string> ColumnNames { get; }
    public IReadOnlyList<LookupTableRow> Rows { get; }

    public LookupTableData(
        string schema,
        string tableName,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<LookupTableRow> rows)
    {
        Schema = schema;
        TableName = tableName;
        ColumnNames = columnNames;
        Rows = rows;
    }

    /// <summary>
    /// Gets the index of a column by name (case-insensitive).
    /// </summary>
    public int GetColumnIndex(string columnName)
    {
        for (int i = 0; i < ColumnNames.Count; i++)
        {
            if (ColumnNames[i].Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Checks if the lookup table has a column with the given suffix.
    /// Common suffixes: "Name", "DisplayName", "SortOrder", "Description"
    /// </summary>
    public int GetColumnIndexBySuffix(string suffix)
    {
        for (int i = 0; i < ColumnNames.Count; i++)
        {
            if (ColumnNames[i].EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    public bool Equals(LookupTableData? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Schema == other.Schema &&
               TableName == other.TableName &&
               ColumnNames.SequenceEqual(other.ColumnNames) &&
               Rows.SequenceEqual(other.Rows);
    }

    public override bool Equals(object? obj) => Equals(obj as LookupTableData);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Schema.GetHashCode();
            hash = hash * 31 + TableName.GetHashCode();
            foreach (var col in ColumnNames)
            {
                hash = hash * 31 + col.GetHashCode();
            }
            foreach (var row in Rows)
            {
                hash = hash * 31 + row.GetHashCode();
            }
            return hash;
        }
    }
}

/// <summary>
/// Represents a single row of data from a MERGE VALUES clause.
/// </summary>
public sealed class LookupTableRow : IEquatable<LookupTableRow>
{
    public IReadOnlyList<string> Values { get; }

    public LookupTableRow(IReadOnlyList<string> values)
    {
        Values = values;
    }

    /// <summary>
    /// Gets the value at the specified column index.
    /// </summary>
    public string? GetValue(int index)
    {
        if (index < 0 || index >= Values.Count) return null;
        return Values[index];
    }

    /// <summary>
    /// Gets the integer value at the specified column index, or null if not a valid int.
    /// </summary>
    public int? GetIntValue(int index)
    {
        var value = GetValue(index);
        if (value == null) return null;
        return int.TryParse(value, out var result) ? result : null;
    }

    public bool Equals(LookupTableRow? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Values.SequenceEqual(other.Values);
    }

    public override bool Equals(object? obj) => Equals(obj as LookupTableRow);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var value in Values)
            {
                hash = hash * 31 + (value?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}
