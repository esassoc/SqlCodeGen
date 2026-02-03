namespace SqlCodeGen.Models;

/// <summary>
/// Represents a column definition from a CREATE TABLE statement.
/// </summary>
public sealed class ColumnDefinition : IEquatable<ColumnDefinition>
{
    public string Name { get; }
    public string SqlType { get; }
    public bool IsNullable { get; }
    public int? MaxLength { get; }
    public bool IsIdentity { get; }
    public bool IsPrimaryKey { get; }
    /// <summary>
    /// The table this column references via foreign key, if any.
    /// </summary>
    public string? ForeignKeyTable { get; }

    public ColumnDefinition(
        string name,
        string sqlType,
        bool isNullable,
        int? maxLength,
        bool isIdentity,
        bool isPrimaryKey,
        string? foreignKeyTable = null)
    {
        Name = name;
        SqlType = sqlType;
        IsNullable = isNullable;
        MaxLength = maxLength;
        IsIdentity = isIdentity;
        IsPrimaryKey = isPrimaryKey;
        ForeignKeyTable = foreignKeyTable;
    }

    /// <summary>
    /// Maps SQL Server types to C# types.
    /// </summary>
    public string ToCSharpType()
    {
        var baseType = SqlType.ToLowerInvariant() switch
        {
            "int" => "int",
            "bigint" => "long",
            "smallint" => "short",
            "tinyint" => "byte",
            "bit" => "bool",
            "decimal" or "numeric" => "decimal",
            "money" or "smallmoney" => "decimal",
            "float" => "double",
            "real" => "float",
            "datetime" or "datetime2" or "smalldatetime" => "DateTime",
            "date" => "DateOnly",
            "time" => "TimeSpan",
            "datetimeoffset" => "DateTimeOffset",
            "uniqueidentifier" => "Guid",
            "varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext" => "string",
            "varbinary" or "binary" or "image" => "byte[]",
            "geometry" => "NetTopologySuite.Geometries.Geometry",
            "geography" => "NetTopologySuite.Geometries.Geometry",
            _ => "object"
        };

        // String and byte[] are reference types, no need for nullable annotation
        if (baseType == "string" || baseType == "byte[]" || baseType == "object" ||
            baseType == "NetTopologySuite.Geometries.Geometry")
        {
            return IsNullable ? baseType + "?" : baseType;
        }

        // Value types need nullable annotation
        return IsNullable ? baseType + "?" : baseType;
    }

    public bool Equals(ColumnDefinition? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name &&
               SqlType == other.SqlType &&
               IsNullable == other.IsNullable &&
               MaxLength == other.MaxLength &&
               IsIdentity == other.IsIdentity &&
               IsPrimaryKey == other.IsPrimaryKey &&
               ForeignKeyTable == other.ForeignKeyTable;
    }

    public override bool Equals(object? obj) => Equals(obj as ColumnDefinition);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Name.GetHashCode();
            hash = hash * 31 + SqlType.GetHashCode();
            hash = hash * 31 + IsNullable.GetHashCode();
            hash = hash * 31 + (MaxLength?.GetHashCode() ?? 0);
            hash = hash * 31 + IsIdentity.GetHashCode();
            hash = hash * 31 + IsPrimaryKey.GetHashCode();
            hash = hash * 31 + (ForeignKeyTable?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
