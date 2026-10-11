# Upload every supported file in a folder to the RAG API.
# Works in Windows PowerShell 5.1 and PowerShell 7+ (uses curl.exe, built into Windows 10+).
#   $env:RAG_API_KEY = '...'; ./scripts/ingest.ps1 -Folder samples/docs
param(
    [string]$Folder = "samples/docs",
    [string]$Url = "http://localhost:5101"
)
$ErrorActionPreference = 'Stop'

$extensions = '.md', '.markdown', '.txt', '.pdf', '.html', '.htm', '.csv', '.json'
$files = Get-ChildItem $Folder -File | Where-Object { $extensions -contains $_.Extension.ToLower() }
if (-not $files) { throw "No supported files in $Folder" }

# The API key is optional locally (the RAG API skips the check when Security:ApiKey is empty).
$headers = @()
if ($env:RAG_API_KEY) { $headers = @('-H', "X-Api-Key: $($env:RAG_API_KEY)") }

$failed = 0
foreach ($file in $files) {
    Write-Host ("Ingesting {0,-40} " -f $file.Name) -NoNewline
    $output = & curl.exe -sS --fail-with-body -X POST "$Url/api/documents" @headers -F "file=@$($file.FullName)" 2>&1
    if ($LASTEXITCODE -eq 0) {
        $result = $output | ConvertFrom-Json
        Write-Host "$($result.chunkCount) chunks" -ForegroundColor Green
    }
    else {
        Write-Host "FAILED: $output" -ForegroundColor Red
        $failed++
    }
}

if ($failed) { exit 1 }
