# Upload every supported file in a folder to the RAG API.
#   $env:RAG_API_KEY = '...'; ./scripts/ingest.ps1 -Folder samples/docs
param(
    [string]$Folder = "samples/docs",
    [string]$Url = "http://localhost:5101"
)
$ErrorActionPreference = 'Stop'
if (-not $env:RAG_API_KEY) { throw "Set RAG_API_KEY (see .env)" }

$extensions = '.md', '.markdown', '.txt', '.pdf', '.html', '.htm', '.csv', '.json'
Get-ChildItem $Folder -File | Where-Object { $extensions -contains $_.Extension.ToLower() } | ForEach-Object {
    Write-Host "Ingesting $($_.Name) ... " -NoNewline
    $result = Invoke-RestMethod -Method Post -Uri "$Url/api/documents" `
        -Headers @{ 'X-Api-Key' = $env:RAG_API_KEY } -Form @{ file = $_ }
    Write-Host "$($result.chunkCount) chunks"
}
