# SqlCodeGen

A Roslyn Source Generator that generates C# EF Core binding classes and TypeScript enums from SQL schema files.

## Features

- **No database connection required** - Reads SQL files directly from your database project
- **C# generation**: Entity binding classes, PrimaryKey wrappers, navigation properties
- **TypeScript generation**: Enum definitions, lookup arrays, dropdown options
- **Incremental generation** - Only regenerates when SQL files change
- **Git history preservation** - Writes to disk with same filenames for clean diffs

## Installation

```xml
<PackageReference Include="SqlCodeGen" Version="1.0.0"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## Configuration

Add to your EF Models project `.csproj`:

```xml
<PropertyGroup>
  <!-- Required: Namespace for generated C# code -->
  <SqlCodeGen_Namespace>MyProject.EFModels.Entities</SqlCodeGen_Namespace>

  <!-- Optional: Output directory for TypeScript enums -->
  <SqlCodeGen_TypeScriptOutputDir>..\MyProject.Web\src\app\shared\generated\enum</SqlCodeGen_TypeScriptOutputDir>

  <!-- Optional: Output directory for C# files (for git history preservation) -->
  <SqlCodeGen_OutputDir>Entities\Generated\ExtensionMethods</SqlCodeGen_OutputDir>

  <!-- Optional: Tables to exclude from generation -->
  <SqlCodeGen_ExcludeTables>DatabaseMigration,__RefactorLog</SqlCodeGen_ExcludeTables>
</PropertyGroup>

<!-- Import props (for project reference) or auto-imported via NuGet -->
<Import Project="..\SqlCodeGen\build\SqlCodeGen.props" />

<!-- SQL files for the Source Generator to process -->
<ItemGroup>
  <AdditionalFiles Include="..\MyProject.Database\dbo\Tables\*.sql" />
  <AdditionalFiles Include="..\MyProject.Database\Scripts\LookupTables\*.sql" />

  <!-- Required for TypeScript generation -->
  <SqlCodeGenTableSql Include="..\MyProject.Database\dbo\Tables\" />
  <SqlCodeGenLookupTableSql Include="..\MyProject.Database\Scripts\LookupTables\" />
</ItemGroup>

<!-- If writing to disk, exclude from compilation (already compiled by generator) -->
<ItemGroup Condition="'$(SqlCodeGen_OutputDir)' != ''">
  <Compile Remove="$(SqlCodeGen_OutputDir)\*.Binding.cs" />
  <Compile Remove="$(SqlCodeGen_OutputDir)\*PrimaryKey.cs" />
</ItemGroup>
```

## What Gets Generated

### For Lookup Tables (tables with MERGE files)

**C# Binding Class** (`{TableName}.Binding.cs`):
- Static readonly instances for each enum value
- `All` list and `AllLookupDictionary` for lookups
- Strongly-typed enum
- `ToType()` and `ToEnum` conversion methods
- Navigation properties to other lookup tables

**TypeScript Enum** (`{table-name}-enum.ts`):
- Enum definition
- `LookupTableEntry[]` array
- `SelectDropdownOption[]` for dropdowns

### For Regular Tables

**C# Binding Class** (`{TableName}.Binding.cs`):
- `FieldLengths` static class with max lengths for string columns
- Navigation properties to lookup tables

### For All Tables

**C# PrimaryKey Wrapper** (`{TableName}PrimaryKey.cs`):
- Strongly-typed primary key wrapper class

## SQL File Requirements

### CREATE TABLE files (`dbo/Tables/*.sql`)

```sql
CREATE TABLE [dbo].[ProjectStage](
    [ProjectStageID] [int] NOT NULL,
    [ProjectStageName] [varchar](100) NOT NULL,
    [ProjectStageDisplayName] [varchar](100) NOT NULL,
    [SortOrder] [int] NOT NULL,
    CONSTRAINT [PK_ProjectStage] PRIMARY KEY CLUSTERED ([ProjectStageID])
)
```

### MERGE files for lookup tables (`Scripts/LookupTables/*.sql`)

```sql
MERGE INTO [dbo].[ProjectStage] AS Target
USING (VALUES
    (1, 'Proposal', 'Proposal', 10),
    (2, 'PlanningDesign', 'Planning/Design', 20),
    (3, 'Implementation', 'Implementation', 30)
) AS Source (ProjectStageID, ProjectStageName, ProjectStageDisplayName, SortOrder)
ON Target.ProjectStageID = Source.ProjectStageID
WHEN MATCHED THEN UPDATE SET ...
WHEN NOT MATCHED THEN INSERT ...;
```

## Development vs Production

This is a **development-only** dependency. The generated files are committed to git, so production builds just compile the checked-in files:

```xml
<!-- Only include generator in Debug builds -->
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <PackageReference Include="SqlCodeGen" Version="1.0.0" ... />
</ItemGroup>
```

## License

MIT
