#!/usr/bin/env bash
# Upload every supported file in a folder to the RAG API.
#   RAG_API_KEY=... ./scripts/ingest.sh [folder] [rag-api-url]
set -euo pipefail

folder="${1:-samples/docs}"
url="${2:-http://localhost:5101}"
: "${RAG_API_KEY:?Set RAG_API_KEY (see .env)}"

shopt -s nullglob nocaseglob
files=("$folder"/*.{md,markdown,txt,pdf,html,htm,csv,json})
if [ ${#files[@]} -eq 0 ]; then
  echo "No supported files in $folder" >&2
  exit 1
fi

for f in "${files[@]}"; do
  printf 'Ingesting %-45s ' "$(basename "$f")"
  curl --fail-with-body -sS -X POST "$url/api/documents" \
    -H "X-Api-Key: $RAG_API_KEY" \
    -F "file=@$f"
  echo
done
