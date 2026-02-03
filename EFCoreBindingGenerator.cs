using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SqlCodeGen.Generators;
using SqlCodeGen.Models;
using SqlCodeGen.Parsers;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace SqlCodeGen;

/// <summary>
/// Roslyn Incremental Source Generator for generating EF Core binding classes
/// and TypeScript enums from SQL schema files.
///
/// WHAT THIS GENERATES:
/// For each lookup table (tables with MERGE files in Scripts/LookupTables/):
/// - {TableName}PrimaryKey.g.cs - EntityPrimaryKey wrapper class
/// - {TableName}.Binding.g.cs - Static instances, enum, type conversion, and navigation properties
/// - TypeScript manifest for enum generation
///
/// FEATURES:
/// - Navigation properties for FK relationships to other lookup tables
/// - SQL N'string' Unicode literal parsing
/// - CONSTRAINT keyword filtering in CREATE TABLE parsing
/// - #nullable enable for generated code
///
/// CONFIGURATION (in consuming .csproj):
/// - SqlCodeGen_Namespace: Target namespace (default: {RootNamespace}.Entities)
/// - SqlCodeGen_ExcludeTables: Comma-separated tables to skip
/// - SqlCodeGen_TypeScriptOutputDir: Output dir for TS enums
/// - SqlCodeGen_OutputDir: Output dir for C# files (for git history preservation)
/// </summary>
[Generator(LanguageNames.CSharp)]
public class EFCoreBindingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get additional files that are SQL table definitions
        var tableFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.Contains("Tables") && file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select((file, ct) => ParseTableFile(file, ct))
            .Where(t => t != null);

        // Get additional files that are lookup table MERGE statements
        var lookupFiles = context.AdditionalTextsProvider
            .Where(file => file.Path.Contains("LookupTables") && file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select((file, ct) => ParseLookupFile(file, ct))
            .Where(l => l != null);

        // Combine tables with lookup data to determine which are lookup tables
        var combinedData = tableFiles.Collect().Combine(lookupFiles.Collect());

        // Get configuration from assembly attributes or global options
        var configProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, ct) => GetConfiguration(options));

        // Combine everything
        var generationInput = combinedData.Combine(configProvider);

        // Register the source output
        context.RegisterSourceOutput(generationInput, GenerateCode);
    }

    private static TableDefinition? ParseTableFile(AdditionalText file, CancellationToken ct)
    {
        var text = file.GetText(ct);
        if (text == null) return null;

        return CreateTableParser.Parse(text.ToString());
    }

    private static LookupTableData? ParseLookupFile(AdditionalText file, CancellationToken ct)
    {
        var text = file.GetText(ct);
        if (text == null) return null;

        return MergeStatementParser.Parse(text.ToString());
    }

    private static GeneratorConfiguration GetConfiguration(AnalyzerConfigOptionsProvider options)
    {
        // Try to get configuration from global options
        options.GlobalOptions.TryGetValue("build_property.SqlCodeGen_Namespace", out var ns);
        options.GlobalOptions.TryGetValue("build_property.SqlCodeGen_TypeScriptOutputDir", out var tsDir);
        options.GlobalOptions.TryGetValue("build_property.SqlCodeGen_ExcludeTables", out var excludeTables);
        options.GlobalOptions.TryGetValue("build_property.SqlCodeGen_OutputDir", out var outputDir);
        options.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDir);

        return new GeneratorConfiguration(
            Namespace: ns ?? "Generated.Entities",
            TypeScriptOutputDir: tsDir,
            ExcludeTables: excludeTables?.Split(',').Select(t => t.Trim()).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
                ?? ImmutableHashSet<string>.Empty,
            OutputDir: outputDir,
            ProjectDir: projectDir
        );
    }

    private static void GenerateCode(
        SourceProductionContext context,
        ((ImmutableArray<TableDefinition?> Tables, ImmutableArray<LookupTableData?> Lookups) Data, GeneratorConfiguration Config) input)
    {
        var (data, config) = input;
        var (tables, lookups) = data;

        // Create lookup dictionary for quick access
        var lookupDict = lookups
            .Where(l => l != null)
            .ToDictionary(l => l!.TableName, l => l!, StringComparer.OrdinalIgnoreCase);

        // Create a set of all lookup table names (for navigation property generation)
        var allLookupTableNames = new HashSet<string>(lookupDict.Keys, StringComparer.OrdinalIgnoreCase);

        // Create a set of ALL table names (lookup and regular) to avoid generating nav properties
        // for columns where the property name matches another table (EF Core handles those FKs)
        var allTableNames = new HashSet<string>(
            tables.Where(t => t != null).Select(t => t!.TableName),
            StringComparer.OrdinalIgnoreCase);

        // Track TypeScript files to generate (for manifest)
        var tsManifest = new List<TypeScriptManifestEntry>();

        // Only process lookup tables (tables with MERGE files)
        // Regular tables already have binding files in the existing project
        foreach (var table in tables)
        {
            if (table == null) continue;
            if (config.ExcludeTables.Contains(table.TableName)) continue;

            // Check if this is a lookup table (has MERGE file)
            var isLookupTable = lookupDict.TryGetValue(table.TableName, out var lookupData);

            if (isLookupTable && lookupData != null)
            {
                // LOOKUP TABLE: Generate full binding with static instances, enum, etc.
                var pkCode = PrimaryKeyCodeGenerator.Generate(table, config.Namespace);
                context.AddSource($"{table.TableName}PrimaryKey.g.cs", SourceText.From(pkCode, Encoding.UTF8));
                // Write to disk without .g suffix to preserve git history
                WriteToOutputDir(config, $"{table.TableName}PrimaryKey.cs", pkCode);

                var bindingCode = LookupTableBindingGenerator.Generate(table, lookupData, config.Namespace, allLookupTableNames, allTableNames);
                context.AddSource($"{table.TableName}.Binding.g.cs", SourceText.From(bindingCode, Encoding.UTF8));
                WriteToOutputDir(config, $"{table.TableName}.Binding.cs", bindingCode);

                // Track for TypeScript generation
                tsManifest.Add(new TypeScriptManifestEntry(
                    table.TableName,
                    TypeScriptEnumGenerator.GetTypeScriptFileName(table.TableName),
                    TypeScriptEnumGenerator.Generate(table, lookupData)
                ));
            }
            else
            {
                // REGULAR TABLE: Generate PrimaryKey class and binding with navigation properties and FieldLengths
                var pkCode = PrimaryKeyCodeGenerator.Generate(table, config.Namespace);
                context.AddSource($"{table.TableName}PrimaryKey.g.cs", SourceText.From(pkCode, Encoding.UTF8));
                WriteToOutputDir(config, $"{table.TableName}PrimaryKey.cs", pkCode);

                var bindingCode = RegularTableBindingGenerator.Generate(table, config.Namespace, allLookupTableNames, allTableNames);
                context.AddSource($"{table.TableName}.Binding.g.cs", SourceText.From(bindingCode, Encoding.UTF8));
                WriteToOutputDir(config, $"{table.TableName}.Binding.cs", bindingCode);
            }
        }

        // Generate TypeScript manifest as a JSON file (for MSBuild target to process)
        if (tsManifest.Count > 0)
        {
            var manifestJson = GenerateTypeScriptManifest(tsManifest);
            context.AddSource("TypeScriptEnums.manifest.g.cs", SourceText.From(
                $"// TypeScript Enum Manifest - {tsManifest.Count} files\n// This is processed by MSBuild to generate .ts files\n/*\n{manifestJson}\n*/",
                Encoding.UTF8));
        }
    }

    private static void WriteToOutputDir(GeneratorConfiguration config, string fileName, string content)
    {
        if (string.IsNullOrEmpty(config.OutputDir) || string.IsNullOrEmpty(config.ProjectDir))
            return;

        try
        {
            var outputPath = Path.IsPathRooted(config.OutputDir)
                ? config.OutputDir
                : Path.Combine(config.ProjectDir, config.OutputDir);

            Directory.CreateDirectory(outputPath);
            var filePath = Path.Combine(outputPath, fileName);

            // Only write if content has changed to avoid unnecessary rebuilds
            if (File.Exists(filePath))
            {
                var existingContent = File.ReadAllText(filePath);
                if (existingContent == content)
                    return;
            }

            File.WriteAllText(filePath, content);
        }
        catch
        {
            // Silently ignore write failures - the in-memory source is still added
        }
    }

    private static string GenerateTypeScriptManifest(List<TypeScriptManifestEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"files\": [");

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var comma = i < entries.Count - 1 ? "," : "";
            // Store content as base64 to avoid JSON escaping issues
            var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Content));
            sb.AppendLine($"    {{ \"tableName\": \"{entry.TableName}\", \"fileName\": \"{entry.FileName}\", \"content\": \"{contentBase64}\" }}{comma}");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }
}

internal record GeneratorConfiguration(
    string Namespace,
    string? TypeScriptOutputDir,
    ImmutableHashSet<string> ExcludeTables,
    string? OutputDir,
    string? ProjectDir
);

internal record TypeScriptManifestEntry(
    string TableName,
    string FileName,
    string Content
);
