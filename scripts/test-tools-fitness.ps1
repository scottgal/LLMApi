#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Comprehensive tool fitness testing script

.DESCRIPTION
    Runs fitness tests on all configured tools, validates their claims,
    generates dummy data with appropriate expectations, and updates the
    fitness RAG data store. Low-fitness tools are automatically flagged
    for evolution/optimization with god-level LLM.

.PARAMETER BaseUrl
    Base URL of the LLMock API instance (default: http://localhost:5116)

.PARAMETER FitnessThreshold
    Fitness score threshold below which tools are flagged for evolution (default: 60.0)

.PARAMETER EvolveTools
    If specified, automatically triggers evolution for low-fitness tools using god-level LLM

.PARAMETER ExportPath
    Path to export the fitness report (default: ./tool-fitness-report.json)

.PARAMETER Verbose
    Enable verbose logging

.EXAMPLE
    .\test-tools-fitness.ps1

.EXAMPLE
    .\test-tools-fitness.ps1 -BaseUrl "http://localhost:5000" -FitnessThreshold 70 -EvolveTools

.EXAMPLE
    .\test-tools-fitness.ps1 -ExportPath "C:\Reports\fitness.json" -Verbose
#>

param(
    [string]$BaseUrl = "http://localhost:5116",
    [double]$FitnessThreshold = 60.0,
    [switch]$EvolveTools,
    [string]$ExportPath = "./tool-fitness-report.json",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

# Color output helpers
function Write-Success { param([string]$Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Error { param([string]$Message) Write-Host "✗ $Message" -ForegroundColor Red }
function Write-Info { param([string]$Message) Write-Host "ℹ $Message" -ForegroundColor Cyan }
function Write-Warning { param([string]$Message) Write-Host "⚠ $Message" -ForegroundColor Yellow }

Write-Host @"
╔════════════════════════════════════════════════════════════════╗
║         Tool Fitness Testing & Evolution System                ║
║         Comprehensive Testing & RAG Optimization               ║
╚════════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Magenta

# Step 1: Verify API is running
Write-Info "Checking API availability at $BaseUrl..."
try {
    $healthCheck = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get -TimeoutSec 5 -ErrorAction SilentlyContinue
    Write-Success "API is running"
} catch {
    Write-Error "API is not accessible at $BaseUrl"
    Write-Info "Please start the API first with: dotnet run --project LLMApi/LLMApi.csproj"
    exit 1
}

# Step 2: Trigger fitness test
Write-Info "Starting comprehensive tool fitness testing..."
Write-Host ""

try {
    $testEndpoint = "$BaseUrl/api/tools/fitness/test"

    Write-Info "Calling endpoint: $testEndpoint"

    $response = Invoke-RestMethod -Uri $testEndpoint -Method Post -ContentType "application/json" -Body "{}"

    if ($null -eq $response) {
        Write-Error "Received null response from fitness test"
        exit 1
    }

    # Display summary
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  FITNESS TEST RESULTS" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    Write-Info "Test Run ID: $($response.testRunId)"
    Write-Info "Total Tools: $($response.totalTools)"
    Write-Success "Passed: $($response.passedTests)"
    if ($response.failedTests -gt 0) {
        Write-Error "Failed: $($response.failedTests)"
    } else {
        Write-Success "Failed: 0"
    }
    Write-Info "Average Fitness: $([math]::Round($response.averageFitness, 2))/100"
    Write-Info "Total Duration: $($response.totalDuration)"
    Write-Host ""

    # Display individual tool results
    Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
    Write-Host "  INDIVIDUAL TOOL RESULTS" -ForegroundColor White
    Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
    Write-Host ""

    $toolResults = $response.toolResults | Sort-Object -Property fitnessScore -Descending

    foreach ($tool in $toolResults) {
        $statusIcon = if ($tool.passed) { "✓" } else { "✗" }
        $statusColor = if ($tool.passed) { "Green" } else { "Red" }

        $fitnessColor = switch ($tool.fitnessScore) {
            {$_ -ge 80} { "Green" }
            {$_ -ge 60} { "Yellow" }
            default { "Red" }
        }

        Write-Host "$statusIcon " -ForegroundColor $statusColor -NoNewline
        Write-Host "$($tool.toolName) " -NoNewline
        Write-Host "[$($tool.toolType)]" -ForegroundColor Gray -NoNewline
        Write-Host " - Fitness: " -NoNewline
        Write-Host "$([math]::Round($tool.fitnessScore, 2))" -ForegroundColor $fitnessColor -NoNewline
        Write-Host "/100 " -NoNewline
        Write-Host "($($tool.executionTimeMs)ms)" -ForegroundColor DarkGray

        if ($Verbose -and -not $tool.passed) {
            foreach ($validation in $tool.validationResults | Where-Object { -not $_.passed }) {
                Write-Host "    ↳ Failed: $($validation.description)" -ForegroundColor DarkRed
                Write-Host "      Expected: $($validation.expected)" -ForegroundColor DarkGray
                Write-Host "      Actual: $($validation.actual)" -ForegroundColor DarkGray
            }
        }
    }

    Write-Host ""

    # Identify low-fitness tools
    $lowFitnessTools = $toolResults | Where-Object { $_.fitnessScore -lt $FitnessThreshold }

    if ($lowFitnessTools.Count -gt 0) {
        Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
        Write-Host "  LOW FITNESS TOOLS (< $FitnessThreshold)" -ForegroundColor Yellow
        Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
        Write-Host ""

        foreach ($tool in $lowFitnessTools) {
            Write-Warning "$($tool.toolName) - Fitness: $([math]::Round($tool.fitnessScore, 2))/100"

            if ($tool.executionError) {
                Write-Host "    Error: $($tool.executionError)" -ForegroundColor DarkRed
            }

            $failedValidations = $tool.validationResults | Where-Object { -not $_.passed }
            if ($failedValidations.Count -gt 0) {
                Write-Host "    Failed Validations: $($failedValidations.Count)" -ForegroundColor DarkYellow
            }
        }

        Write-Host ""

        # Evolution prompt
        if ($EvolveTools) {
            Write-Info "Evolution enabled. Triggering god-level LLM optimization..."
            Write-Host ""

            $evolveEndpoint = "$BaseUrl/api/tools/fitness/evolve"
            $evolveBody = @{
                threshold = $FitnessThreshold
            } | ConvertTo-Json

            try {
                $evolutionResponse = Invoke-RestMethod -Uri $evolveEndpoint -Method Post -ContentType "application/json" -Body $evolveBody

                Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
                Write-Host "  EVOLUTION RESULTS" -ForegroundColor Cyan
                Write-Host "─────────────────────────────────────────────────────" -ForegroundColor Gray
                Write-Host ""

                foreach ($evolution in $evolutionResponse.evolutionResults) {
                    if ($evolution.success) {
                        Write-Success "$($evolution.toolName) - Evolution Complete"
                        Write-Host ""
                        Write-Host "Original Fitness: $([math]::Round($evolution.originalFitness, 2))/100" -ForegroundColor Gray
                        Write-Host ""
                        Write-Host "Recommendations:" -ForegroundColor White
                        Write-Host $evolution.recommendations -ForegroundColor DarkGray
                        Write-Host ""
                        Write-Host "─────────────────────────────────────────────────────" -ForegroundColor DarkGray
                    } else {
                        Write-Error "$($evolution.toolName) - Evolution Failed: $($evolution.error)"
                    }
                }
            } catch {
                Write-Error "Failed to trigger evolution: $($_.Exception.Message)"
            }
        } else {
            Write-Info "To trigger automatic evolution, run with -EvolveTools flag"
        }
    } else {
        Write-Success "All tools meet the fitness threshold! 🎉"
    }

    # Export report
    Write-Host ""
    Write-Info "Exporting fitness report to: $ExportPath"

    $response | ConvertTo-Json -Depth 10 | Out-File -FilePath $ExportPath -Encoding UTF8

    Write-Success "Report exported successfully"

    # RAG data location
    Write-Host ""
    Write-Info "RAG-optimized data has been stored in the fitness RAG database"
    Write-Info "Location: %LocalAppData%\LLMockApi\ToolFitness\"

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Success "Tool fitness testing complete!"
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan

    exit 0

} catch {
    Write-Error "Fitness testing failed: $($_.Exception.Message)"
    if ($Verbose) {
        Write-Host $_.Exception.StackTrace -ForegroundColor DarkRed
    }
    exit 1
}