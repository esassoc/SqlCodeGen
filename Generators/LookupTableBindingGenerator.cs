using System.Text;
using SqlCodeGen.Models;

namespace SqlCodeGen.Generators;

/// <summary>
/// Generates {TableName}.Binding.cs files for lookup tables.
/// These contain enum, static instances, and type-safe enum conversion.
/// </summary>
public static class LookupTableBindingGenerator
{
    /// <summary>
    /// Generates the Binding class code for a lookup table.
    /// </summary>
    /// <param name="table">The table definition from CREATE TABLE parsing.</param>
    /// <param name="lookupData">The lookup data from MERGE statement parsing.</param>
    /// <param name="namespace">The target namespace for generated code.</param>
    /// <param name="allLookupTableNames">Set of all lookup table names, used to generate navigation properties.</param>
    /// <param name="allTableNames">Set of all table names, used to avoid generating properties that conflict with EF Core.</param>
    public static string Generate(TableDefinition table, LookupTableData lookupData, string @namespace, HashSet<string> allLookupTableNames, HashSet<string> allTableNames)
    {
        var tableName = table.TableName;
        var pkColumn = table.PrimaryKeyColumn ?? $"{tableName}ID";

        // Find the Name column (the system/enum name, NOT the DisplayName)
        // Convention: {TableName}Name is the system name, {TableName}DisplayName is the display name
        var nameColIndex = FindSystemNameColumnIndex(lookupData, tableName);
        var displayNameColIndex = FindDisplayNameColumnIndex(lookupData, tableName);

        // If no separate DisplayName, use Name for both
        if (displayNameColIndex < 0)
        {
            displayNameColIndex = nameColIndex;
        }

        // Find the ID column (usually first column ending with ID)
        var idColIndex = lookupData.GetColumnIndex(pkColumn);
        if (idColIndex < 0)
        {
            idColIndex = lookupData.GetColumnIndexBySuffix("ID");
        }

        // Get constructor parameters (columns except computed ones)
        var constructorParams = GetConstructorParameters(table, lookupData);

        var sb = new StringBuilder();
        sb.AppendLine("//  IMPORTANT:");
        sb.AppendLine("//  This file is generated. Your changes will be lost.");
        sb.AppendLine("//  Use the corresponding partial class for customizations.");
        sb.AppendLine($"//  Source Table: [{table.Schema}].[{table.TableName}]");
        sb.AppendLine("using System.Collections.ObjectModel;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {@namespace}");
        sb.AppendLine("{");

        // Main abstract class
        sb.AppendLine($"    public abstract partial class {tableName} : IHavePrimaryKey");
        sb.AppendLine("    {");

        // Static readonly instances
        foreach (var row in lookupData.Rows)
        {
            var name = SanitizeIdentifier(row.GetValue(nameColIndex) ?? "Unknown");
            sb.AppendLine($"        public static readonly {tableName}{name} {name} = {tableName}{name}.Instance;");
        }
        sb.AppendLine();

        // All and AllLookupDictionary
        sb.AppendLine($"        public static readonly List<{tableName}> All;");
        sb.AppendLine($"        public static readonly ReadOnlyDictionary<int, {tableName}> AllLookupDictionary;");
        sb.AppendLine();

        // Static constructor
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Static type constructor to coordinate static initialization order");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        static {tableName}()");
        sb.AppendLine("        {");
        var instanceNames = lookupData.Rows.Select(r => SanitizeIdentifier(r.GetValue(nameColIndex) ?? "Unknown")).ToList();
        sb.AppendLine($"            All = new List<{tableName}> {{ {string.Join(", ", instanceNames)} }};");
        sb.AppendLine($"            AllLookupDictionary = new ReadOnlyDictionary<int, {tableName}>(All.ToDictionary(x => x.{pkColumn}));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Protected constructor
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Protected constructor only for use in instantiating the set of static lookup values that match database");
        sb.AppendLine("        /// </summary>");
        var ctorParams = string.Join(", ", constructorParams.Select(p => $"{p.CSharpType} {p.LowerCamelName}"));
        sb.AppendLine($"        protected {tableName}({ctorParams})");
        sb.AppendLine("        {");
        foreach (var param in constructorParams)
        {
            sb.AppendLine($"            {param.Name} = {param.LowerCamelName};");
        }
        sb.AppendLine("        }");
        sb.AppendLine();

        // Properties
        foreach (var param in constructorParams)
        {
            if (param.Name == pkColumn)
            {
                sb.AppendLine("        [Key]");
            }
            sb.AppendLine($"        public {param.CSharpType} {param.Name} {{ get; private set; }}");
        }
        sb.AppendLine("        [NotMapped]");
        sb.AppendLine($"        public int PrimaryKey {{ get {{ return {pkColumn}; }} }}");
        sb.AppendLine();

        // Navigation properties for foreign keys to other lookup tables
        GenerateNavigationProperties(sb, table, pkColumn, constructorParams, allLookupTableNames, allTableNames);

        // Equals methods
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Enum types are equal by primary key");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        public bool Equals({tableName} other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (other == null)");
        sb.AppendLine("            {");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine($"            return other.{pkColumn} == {pkColumn};");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Enum types are equal by primary key");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public override bool Equals(object obj)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return Equals(obj as {tableName});");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Enum types are equal by primary key");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine($"            return {pkColumn};");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Equality operators
        sb.AppendLine($"        public static bool operator ==({tableName} left, {tableName} right)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Equals(left, right);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public static bool operator !=({tableName} left, {tableName} right)");
        sb.AppendLine("        {");
        sb.AppendLine("            return !Equals(left, right);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // ToEnum property
        sb.AppendLine($"        public {tableName}Enum ToEnum => ({tableName}Enum)GetHashCode();");
        sb.AppendLine();

        // ToType methods
        sb.AppendLine($"        public static {tableName} ToType(int enumValue)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return ToType(({tableName}Enum)enumValue);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine($"        public static {tableName} ToType({tableName}Enum enumValue)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (enumValue)");
        sb.AppendLine("            {");
        foreach (var row in lookupData.Rows)
        {
            var name = SanitizeIdentifier(row.GetValue(nameColIndex) ?? "Unknown");
            sb.AppendLine($"                case {tableName}Enum.{name}:");
            sb.AppendLine($"                    return {name};");
        }
        sb.AppendLine("                default:");
        sb.AppendLine("                    throw new ArgumentException(\"Unable to map Enum: {enumValue}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Enum definition
        sb.AppendLine($"    public enum {tableName}Enum");
        sb.AppendLine("    {");
        foreach (var row in lookupData.Rows)
        {
            var id = row.GetValue(idColIndex) ?? "0";
            var name = SanitizeIdentifier(row.GetValue(nameColIndex) ?? "Unknown");
            sb.AppendLine($"        {name} = {id},");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // Concrete subclasses for each value
        foreach (var row in lookupData.Rows)
        {
            var name = SanitizeIdentifier(row.GetValue(nameColIndex) ?? "Unknown");

            // Build constructor parameters with actual values
            var ctorParamDecl = string.Join(", ", constructorParams.Select(p => $"{p.CSharpType} {p.LowerCamelName}"));
            var baseCall = string.Join(", ", constructorParams.Select(p => p.LowerCamelName));

            // Build actual values for this row
            var values = new List<string>();
            for (int i = 0; i < constructorParams.Count; i++)
            {
                var param = constructorParams[i];
                var colIndex = lookupData.GetColumnIndex(param.Name);
                var value = colIndex >= 0 ? row.GetValue(colIndex) : "null";

                // Handle SQL NULL values
                if (value != null && value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add("null");
                    continue;
                }

                // Format value based on type
                // Check for string types (including nullable string?)
                if (param.CSharpType.StartsWith("string"))
                {
                    values.Add($"@\"{EscapeString(value ?? "")}\"");
                }
                else if (param.CSharpType == "bool" || param.CSharpType == "bool?")
                {
                    // Convert SQL bit values (0/1) to C# bool (false/true)
                    var boolValue = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    values.Add(boolValue ? "true" : "false");
                }
                else if (param.CSharpType == "decimal" || param.CSharpType == "decimal?")
                {
                    // Decimal literals need 'm' suffix
                    values.Add((value ?? "0") + "m");
                }
                else
                {
                    values.Add(value ?? "0");
                }
            }

            sb.AppendLine($"    public partial class {tableName}{name} : {tableName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private {tableName}{name}({ctorParamDecl}) : base({baseCall}) {{}}");
            sb.AppendLine($"        public static readonly {tableName}{name} Instance = new {tableName}{name}({string.Join(", ", values)});");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static List<ConstructorParam> GetConstructorParameters(TableDefinition table, LookupTableData lookupData)
    {
        var result = new List<ConstructorParam>();

        foreach (var colName in lookupData.ColumnNames)
        {
            // Find the column in the table definition to get the C# type
            var column = table.Columns.FirstOrDefault(c =>
                c.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));

            var csharpType = column?.ToCSharpType() ?? "string";
            var lowerCamelName = char.ToLowerInvariant(colName[0]) + colName.Substring(1);

            result.Add(new ConstructorParam(colName, csharpType, lowerCamelName));
        }

        return result;
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Sanitizes a string value to be a valid C# identifier.
    /// Removes invalid characters and ensures the name starts with a letter or underscore.
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
            // Skip spaces, parentheses, slashes, etc.
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

    private record ConstructorParam(string Name, string CSharpType, string LowerCamelName);

    /// <summary>
    /// Finds the system name column index (e.g., BenefitCategoryName, not BenefitCategoryDisplayName).
    /// Convention: Look for {TableName}Name column that is NOT {TableName}DisplayName.
    /// </summary>
    private static int FindSystemNameColumnIndex(LookupTableData lookupData, string tableName)
    {
        // First, try to find exact match: {TableName}Name
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
    /// Finds the display name column index (e.g., BenefitCategoryDisplayName).
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
    /// Generates navigation properties for foreign keys that reference other lookup tables.
    /// Handles both simple naming (ProjectStageID → ProjectStage) and prefixed naming
    /// (AssessedAsTreatmentBMPTypeID → AssessedAsTreatmentBMPType referencing TreatmentBMPType).
    /// </summary>
    private static void GenerateNavigationProperties(
        StringBuilder sb,
        TableDefinition table,
        string pkColumn,
        List<ConstructorParam> constructorParams,
        HashSet<string> allLookupTableNames,
        HashSet<string> allTableNames)
    {
        var generatedAny = false;

        foreach (var param in constructorParams)
        {
            // Skip if not an ID column
            if (!param.Name.EndsWith("ID", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip the primary key column
            if (param.Name.Equals(pkColumn, StringComparison.OrdinalIgnoreCase))
                continue;

            // Extract the property name by removing "ID" suffix
            var propertyName = param.Name.Substring(0, param.Name.Length - 2);

            // Find the column definition to check for explicit FK reference
            var column = table.Columns.FirstOrDefault(c =>
                c.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase));

            // Find which lookup table this references
            // First, check if the column has an explicit FK reference from the CREATE TABLE
            string? referencedTable = null;
            if (column != null &&
                !string.IsNullOrEmpty(column.ForeignKeyTable) &&
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
            var isNullable = param.CSharpType.EndsWith("?");

            if (!generatedAny)
            {
                sb.AppendLine("        // Navigation properties for foreign keys to other lookup tables");
                generatedAny = true;
            }

            if (isNullable)
            {
                // Nullable FK: return null if the ID is null
                sb.AppendLine($"        public {referencedTable}? {propertyName} => {param.Name}.HasValue ? {referencedTable}.AllLookupDictionary[{param.Name}.Value] : null;");
            }
            else
            {
                // Non-nullable FK: direct lookup
                sb.AppendLine($"        public {referencedTable} {propertyName} => {referencedTable}.AllLookupDictionary[{param.Name}];");
            }
        }

        if (generatedAny)
        {
            sb.AppendLine();
        }
    }
}
