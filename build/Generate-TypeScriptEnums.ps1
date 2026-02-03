# Generate-TypeScriptEnums.ps1
# Generates TypeScript enum files from SQL lookup table definitions.
#
# Usage:
#   .\Generate-TypeScriptEnums.ps1 -TableSqlDir "path\to\Tables" -LookupSqlDir "path\to\LookupTables" -OutputDir "path\to\output"

param(
    [Parameter(Mandatory=$true)]
    [string]$TableSqlDir,

    [Parameter(Mandatory=$true)]
    [string]$LookupSqlDir,

    [Parameter(Mandatory=$true)]
    [string]$OutputDir,

    [string]$ExcludeTables = ""
)

$ErrorActionPreference = "Stop"

# Parse exclude list
$excludeList = @()
if ($ExcludeTables) {
    $excludeList = $ExcludeTables -split ',' | ForEach-Object { $_.Trim().ToLower() }
}

# Helper function to convert PascalCase to kebab-case
function ConvertTo-KebabCase {
    param([string]$text)  # Note: Don't use $input - it's a reserved PowerShell variable

    $result = ""
    for ($i = 0; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if ([char]::IsUpper($c)) {
            if ($i -gt 0) {
                $result += "-"
            }
            $result += [char]::ToLower($c)
        } else {
            $result += $c
        }
    }
    return $result
}

# Helper function to pluralize table names
function Get-PluralName {
    param([string]$name)

    if ($name -match 'y$' -and $name -notmatch '(ay|ey|oy|uy)$') {
        return $name.Substring(0, $name.Length - 1) + "ies"
    }
    if ($name -match '(s|x|ch|sh)$') {
        return $name + "es"
    }
    return $name + "s"
}

# Parse MERGE statement to get lookup data
function Parse-MergeStatement {
    param([string]$sql)

    # Extract table name
    if ($sql -notmatch 'MERGE\s+INTO\s+\[?(\w+)\]?\.\[?(\w+)\]?\s+AS\s+Target') {
        return $null
    }
    $schema = $Matches[1]
    $tableName = $Matches[2]

    # Extract column names from AS Source (...)
    if ($sql -notmatch 'AS\s+Source\s*\(\s*([^)]+)\s*\)') {
        return $null
    }
    $columnNames = $Matches[1] -split ',' | ForEach-Object { $_.Trim().Trim('[', ']') }

    # Extract VALUES section
    if ($sql -notmatch 'USING\s*\(\s*VALUES([\s\S]*?)AS\s+Source') {
        return $null
    }
    $valuesSection = $Matches[1].Trim()

    # Parse each row of values using character-by-character parsing
    # to handle parentheses inside quoted strings
    $rows = @()
    $inString = $false
    $parenDepth = 0
    $rowStart = -1

    for ($i = 0; $i -lt $valuesSection.Length; $i++) {
        $c = $valuesSection[$i]

        if ($inString) {
            if ($c -eq "'") {
                # Check for escaped quote ''
                if ($i + 1 -lt $valuesSection.Length -and $valuesSection[$i + 1] -eq "'") {
                    $i++
                    continue
                }
                $inString = $false
            }
        } else {
            if ($c -eq "'") {
                $inString = $true
            } elseif ($c -eq "(") {
                if ($parenDepth -eq 0) {
                    $rowStart = $i + 1
                }
                $parenDepth++
            } elseif ($c -eq ")") {
                $parenDepth--
                if ($parenDepth -eq 0 -and $rowStart -ge 0) {
                    $rowContent = $valuesSection.Substring($rowStart, $i - $rowStart)

                    # Parse comma-separated values, respecting quoted strings
                    $values = @()
                    $inStringVal = $false
                    $current = ""

                    for ($j = 0; $j -lt $rowContent.Length; $j++) {
                        $ch = $rowContent[$j]

                        if ($inStringVal) {
                            if ($ch -eq "'") {
                                # Check for escaped quote ''
                                if ($j + 1 -lt $rowContent.Length -and $rowContent[$j + 1] -eq "'") {
                                    $current += "'"
                                    $j++
                                    continue
                                }
                                $inStringVal = $false
                            } else {
                                $current += $ch
                            }
                        } else {
                            if ($ch -eq "'") {
                                # Check for N'string' Unicode prefix - remove trailing N
                                if ($current -match 'N$') {
                                    $current = $current.Substring(0, $current.Length - 1)
                                }
                                $inStringVal = $true
                            } elseif ($ch -eq ",") {
                                $values += $current.Trim()
                                $current = ""
                            } elseif (-not [char]::IsWhiteSpace($ch)) {
                                $current += $ch
                            }
                        }
                    }
                    $values += $current.Trim()
                    $rows += ,@($values)
                    $rowStart = -1
                }
            }
        }
    }

    return @{
        Schema = $schema
        TableName = $tableName
        ColumnNames = $columnNames
        Rows = $rows
    }
}

# Generate TypeScript content
function Generate-TypeScriptEnum {
    param($lookupData)

    $tableName = $lookupData.TableName
    $columnNames = $lookupData.ColumnNames
    $rows = $lookupData.Rows

    # Find column indices using the same logic as the C# generator
    $idColIndex = -1
    $nameColIndex = -1
    $displayNameColIndex = -1
    $sortOrderColIndex = -1

    # First pass: look for exact matches based on table name convention
    $exactNameCol = "${tableName}Name"
    $exactDisplayNameCol = "${tableName}DisplayName"

    for ($i = 0; $i -lt $columnNames.Count; $i++) {
        $col = $columnNames[$i]
        if ($col -eq $exactNameCol) { $nameColIndex = $i }
        if ($col -eq $exactDisplayNameCol) { $displayNameColIndex = $i }
        if ($col -like "*ID" -and $idColIndex -eq -1) { $idColIndex = $i }
        if ($col -like "*SortOrder") { $sortOrderColIndex = $i }
    }

    # Second pass: fallback to suffix matching if exact match not found
    if ($nameColIndex -eq -1) {
        for ($i = 0; $i -lt $columnNames.Count; $i++) {
            $col = $columnNames[$i]
            # Look for column ending with "Name" but NOT "DisplayName"
            if ($col -like "*Name" -and $col -notlike "*DisplayName" -and $nameColIndex -eq -1) {
                $nameColIndex = $i
            }
        }
    }
    if ($displayNameColIndex -eq -1) {
        for ($i = 0; $i -lt $columnNames.Count; $i++) {
            $col = $columnNames[$i]
            if ($col -like "*DisplayName") { $displayNameColIndex = $i }
        }
    }

    if ($displayNameColIndex -eq -1) { $displayNameColIndex = $nameColIndex }
    if ($nameColIndex -eq -1) { $nameColIndex = 1 } # Default to second column
    if ($idColIndex -eq -1) { $idColIndex = 0 } # Default to first column

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("//  IMPORTANT:")
    [void]$sb.AppendLine("//  This file is generated. Your changes will be lost.")
    [void]$sb.AppendLine("//  Source Table: [$($lookupData.Schema)].[$tableName]")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine('import { LookupTableEntry } from "src/app/shared/models/lookup-table-entry";')
    [void]$sb.AppendLine('import { SelectDropdownOption } from "src/app/shared/components/forms/form-field/form-field.component"')
    [void]$sb.AppendLine("")

    # Enum
    [void]$sb.AppendLine("export enum ${tableName}Enum {")
    foreach ($row in $rows) {
        $id = $row[$idColIndex]
        $name = $row[$nameColIndex]
        [void]$sb.AppendLine("  $name = $id,")
    }
    [void]$sb.AppendLine("}")
    [void]$sb.AppendLine("")

    # LookupTableEntry array
    $pluralName = Get-PluralName $tableName
    [void]$sb.AppendLine("export const ${pluralName}: LookupTableEntry[]  = [")

    foreach ($row in $rows) {
        $id = $row[$idColIndex]
        $name = $row[$nameColIndex]
        $displayName = if ($displayNameColIndex -ge 0 -and $displayNameColIndex -lt $row.Count) { $row[$displayNameColIndex] } else { $name }

        # Calculate sort order
        if ($sortOrderColIndex -ge 0 -and $sortOrderColIndex -lt $row.Count) {
            $sortOrder = $row[$sortOrderColIndex]
        } else {
            $sortOrder = [int]$id * 10
        }

        # Escape display name
        $displayName = $displayName -replace '\\', '\\\\' -replace '"', '\"'

        [void]$sb.AppendLine("  { Name: `"$name`", DisplayName: `"$displayName`", Value: $id, SortOrder: $sortOrder },")
    }
    [void]$sb.AppendLine("];")

    # SelectDropdownOption array
    [void]$sb.AppendLine("export const ${pluralName}AsSelectDropdownOptions = ${pluralName}.map((x) => ({ Value: x.Value, Label: x.DisplayName, SortOrder: x.SortOrder } as SelectDropdownOption));")

    return $sb.ToString()
}

# Main script

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Get all lookup table SQL files
$lookupFiles = Get-ChildItem -Path $LookupSqlDir -Filter "*.sql" -File

$generatedCount = 0

foreach ($file in $lookupFiles) {
    # Extract table name from filename (remove dbo. prefix and .sql suffix)
    $tableName = $file.BaseName -replace '^dbo\.', ''

    # Check if excluded
    if ($excludeList -contains $tableName.ToLower()) {
        Write-Verbose "Skipping excluded table: $tableName"
        continue
    }

    # Parse the MERGE statement
    $sql = Get-Content $file.FullName -Raw
    $lookupData = Parse-MergeStatement $sql

    if ($null -eq $lookupData) {
        Write-Warning "Could not parse MERGE statement in: $($file.Name)"
        continue
    }

    # Generate TypeScript
    $tsContent = Generate-TypeScriptEnum $lookupData

    # Write to file
    $tsFileName = (ConvertTo-KebabCase $tableName) + "-enum.ts"
    $tsFilePath = Join-Path $OutputDir $tsFileName

    Set-Content -Path $tsFilePath -Value $tsContent -Encoding UTF8

    $generatedCount++
}

Write-Host "SqlCodeGen: Generated $generatedCount TypeScript enum files"
