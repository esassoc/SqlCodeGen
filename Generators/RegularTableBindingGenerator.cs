using System.Text;
using SqlCodeGen.Models;

namespace SqlCodeGen.Generators;

/// <summary>
/// Generates {TableName}.Binding.cs files for regular (non-lookup) tables.
/// These contain PrimaryKey property and FieldLengths static class.
/// </summary>
public static class RegularTableBindingGenerator
{
    /// <summary>
    /// Generates the Binding class code for a regular table.
    /// </summary>
    /// <param name="table">The table definition from CREATE TABLE parsing.</param>
    /// <param name="namespace">The target namespace for generated code.</param>
    /// <param name="allLookupTableNames">Set of all lookup table names, used to generate navigation properties.</param>
    /// <param name="allTableNames">Set of all table names, used to avoid generating properties that conflict with EF Core.</param>
    public static string Generate(TableDefinition table, string @namespace, HashSet<string> allLookupTableNames, HashSet<string> allTableNames)
    {
        var tableName = table.TableName;
        var pkColumn = table.PrimaryKeyColumn ?? $"{tableName}ID";
        var stringColumns = table.GetStringColumnsWithLengths().ToList();

        var sb = new StringBuilder();
        sb.AppendLine("//  IMPORTANT:");
        sb.AppendLine("//  This file is generated. Your changes will be lost.");
        sb.AppendLine("//  Use the corresponding partial class for customizations.");
        sb.AppendLine($"//  Source Table: [{table.Schema}].[{table.TableName}]");
        sb.AppendLine($"namespace {@namespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {tableName} : IHavePrimaryKey");
        sb.AppendLine("    {");
        sb.AppendLine($"        public int PrimaryKey => {pkColumn};");

        // Generate navigation properties for foreign keys to lookup tables
        GenerateNavigationProperties(sb, table, pkColumn, allLookupTableNames, allTableNames);

        // Only add FieldLengths if there are string columns with lengths
        if (stringColumns.Any())
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("        public static class FieldLengths");
            sb.AppendLine("        {");
            foreach (var (name, maxLength) in stringColumns)
            {
                // Skip MAX (-1) columns
                if (maxLength > 0)
                {
                    sb.AppendLine($"            public const int {name} = {maxLength};");
                }
            }
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the base namespace from a full namespace.
    /// </summary>
    private static string GetBaseNamespace(string @namespace)
    {
        var lastDot = @namespace.LastIndexOf('.');
        if (lastDot > 0)
        {
            return @namespace.Substring(0, lastDot);
        }
        return @namespace;
    }

    /// <summary>
    /// Generates navigation properties for foreign keys that reference lookup tables.
    /// Handles both simple naming (ProjectStageID → ProjectStage) and prefixed naming
    /// (AssessedAsTreatmentBMPTypeID → AssessedAsTreatmentBMPType referencing TreatmentBMPType).
    /// </summary>
    private static void GenerateNavigationProperties(
        StringBuilder sb,
        TableDefinition table,
        string pkColumn,
        HashSet<string> allLookupTableNames,
        HashSet<string> allTableNames)
    {
        foreach (var column in table.Columns)
        {
            // Skip if not an ID column
            if (!column.Name.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip the primary key column
            if (column.Name.Equals(pkColumn, StringComparison.OrdinalIgnoreCase))
                continue;

            // Extract the property name by removing "ID" suffix
            var propertyName = column.Name.Substring(0, column.Name.Length - 2);

            // Find which lookup table this references
            // First, check if the column has an explicit FK reference from the CREATE TABLE
            string? referencedTable = null;
            if (!string.IsNullOrEmpty(column.ForeignKeyTable) &&
                allLookupTableNames.Contains(column.ForeignKeyTable))
            {
                // Use the explicit FK reference if it points to a lookup table
                referencedTable = column.ForeignKeyTable;
            }
            else
            {
                // Fall back to heuristic matching based on column name patterns:
                // 1. Exact: CommodityID → property Commodity matches lookup Commodity
                // 2. Prefixed: AssessedAsTreatmentBMPTypeObsoleteID → property ends with TreatmentBMPTypeObsolete
                // 3. Aliased suffix: ChartTypeID → IndicatorChartType ends with ChartType
                // 4. Aliased prefix: CommodityConvertedToID → property starts with Commodity (and is longer)
                foreach (var lookupTable in allLookupTableNames)
                {
                    bool matches = false;

                    // Pattern 1: Exact match
                    if (propertyName.Equals(lookupTable, StringComparison.OrdinalIgnoreCase))
                    {
                        matches = true;
                    }
                    // Pattern 2: Prefixed FK (property ends with lookup table name)
                    else if (propertyName.EndsWith(lookupTable, StringComparison.OrdinalIgnoreCase))
                    {
                        matches = true;
                    }
                    // Pattern 3: Aliased suffix FK (lookup table name ends with property)
                    else if (lookupTable.EndsWith(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        matches = true;
                    }
                    // Pattern 4: Aliased prefix FK (property starts with lookup table name and is longer)
                    else if (propertyName.Length > lookupTable.Length &&
                             propertyName.StartsWith(lookupTable, StringComparison.OrdinalIgnoreCase))
                    {
                        matches = true;
                    }

                    if (matches)
                    {
                        // Prefer longer matching table name
                        if (referencedTable == null || lookupTable.Length > referencedTable.Length)
                        {
                            referencedTable = lookupTable;
                        }
                    }
                }
            }

            if (referencedTable == null)
                continue;

            // Skip if the property name matches another table name (not the same as the lookup table)
            // This avoids conflicts with EF Core's navigation properties for regular table FKs
            // e.g., TransactionTypeCommodityID would match lookup table Commodity (ends with CommodityID)
            // but property name TransactionTypeCommodity is itself a table, so EF Core handles that FK
            if (!propertyName.Equals(referencedTable, StringComparison.OrdinalIgnoreCase) &&
                allTableNames.Contains(propertyName))
            {
                continue;
            }

            // Determine if the FK is nullable
            var isNullable = column.IsNullable;

            if (isNullable)
            {
                // Nullable FK: return null if the ID is null
                sb.AppendLine($"        public {referencedTable}? {propertyName} => {column.Name}.HasValue ? {referencedTable}.AllLookupDictionary[{column.Name}.Value] : null;");
            }
            else
            {
                // Non-nullable FK: direct lookup
                sb.AppendLine($"        public {referencedTable} {propertyName} => {referencedTable}.AllLookupDictionary[{column.Name}];");
            }
        }
    }
}
