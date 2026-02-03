namespace SqlCodeGen.Models;

/// <summary>
/// Represents a parsed SQL table definition from a CREATE TABLE statement.
/// </summary>
public sealed class TableDefinition : IEquatable<TableDefinition>
{
    public string Schema { get; }
    public string TableName { get; }
    public IReadOnlyList<ColumnDefinition> Columns { get; }
    public string? PrimaryKeyColumn { get; }

    public TableDefinition(string schema, string tableName, IReadOnlyList<ColumnDefinition> columns, string? primaryKeyColumn)
    {
        Schema = schema;
        TableName = tableName;
        Columns = columns;
        PrimaryKeyColumn = primaryKeyColumn;
    }

    /// <summary>
    /// Gets the primary key column definition, or null if no primary key is defined.
    /// </summary>
    public ColumnDefinition? GetPrimaryKeyColumnDefinition()
    {
        if (PrimaryKeyColumn == null) return null;
        return Columns.FirstOrDefault(c => c.Name.Equals(PrimaryKeyColumn, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all varchar/nvarchar columns with their max lengths for FieldLengths generation.
    /// </summary>
    public IEnumerable<(string Name, int MaxLength)> GetStringColumnsWithLengths()
    {
        return Columns
            .Where(c => c.MaxLength.HasValue && c.MaxLength > 0 &&
                       (c.SqlType.Equals("varchar", StringComparison.OrdinalIgnoreCase) ||
                        c.SqlType.Equals("nvarchar", StringComparison.OrdinalIgnoreCase) ||
                        c.SqlType.Equals("char", StringComparison.OrdinalIgnoreCase) ||
                        c.SqlType.Equals("nchar", StringComparison.OrdinalIgnoreCase)))
            .Select(c => (c.Name, c.MaxLength!.Value));
    }

    public bool Equals(TableDefinition? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Schema == other.Schema &&
               TableName == other.TableName &&
               Columns.SequenceEqual(other.Columns) &&
               PrimaryKeyColumn == other.PrimaryKeyColumn;
    }

    public override bool Equals(object? obj) => Equals(obj as TableDefinition);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Schema.GetHashCode();
            hash = hash * 31 + TableName.GetHashCode();
            foreach (var col in Columns)
            {
                hash = hash * 31 + col.GetHashCode();
            }
            if (PrimaryKeyColumn != null)
            {
                hash = hash * 31 + PrimaryKeyColumn.GetHashCode();
            }
            return hash;
        }
    }
}
