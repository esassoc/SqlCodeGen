using SqlCodeGen.Models;

namespace SqlCodeGen.Generators;

/// <summary>
/// Generates {TableName}PrimaryKey.cs files for all tables.
/// </summary>
public static class PrimaryKeyCodeGenerator
{
    /// <summary>
    /// Generates the PrimaryKey class code for a table.
    /// </summary>
    public static string Generate(TableDefinition table, string @namespace)
    {
        var tableName = table.TableName;
        var lowerCamelName = char.ToLowerInvariant(tableName[0]) + tableName.Substring(1);

        return $@"//  IMPORTANT:
//  This file is generated. Your changes will be lost.
//  Use the corresponding partial class for customizations.
//  Source Table: {table.Schema}.{table.TableName}


namespace {@namespace}
{{
    public class {tableName}PrimaryKey : EntityPrimaryKey<{tableName}>
    {{
        public {tableName}PrimaryKey() : base(){{}}
        public {tableName}PrimaryKey(int primaryKeyValue) : base(primaryKeyValue){{}}
        public {tableName}PrimaryKey({tableName} {lowerCamelName}) : base({lowerCamelName}){{}}

        public static implicit operator {tableName}PrimaryKey(int primaryKeyValue)
        {{
            return new {tableName}PrimaryKey(primaryKeyValue);
        }}

        public static implicit operator {tableName}PrimaryKey({tableName} {lowerCamelName})
        {{
            return new {tableName}PrimaryKey({lowerCamelName});
        }}
    }}
}}
";
    }

    /// <summary>
    /// Extracts the base namespace from a full namespace.
    /// For example: LtInfo.EFModels.Entities -> LtInfo.EFModels
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
}
