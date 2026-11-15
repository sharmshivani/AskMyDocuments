#!/usr/bin/env bash
# Usage: source scripts/set-env.sh   (run from repo root after `terraform apply`)
tf() { terraform -chdir=infra output -raw "$1"; }
export AZURE_OPENAI_ENDPOINT="$(tf openai_endpoint)"
export AZURE_OPENAI_EMBEDDING_DEPLOYMENT="$(tf embedding_deployment)"
export AZURE_OPENAI_CHAT_DEPLOYMENT="$(tf chat_deployment)"
export AZURE_SEARCH_ENDPOINT="$(tf search_endpoint)"
export AZURE_SEARCH_INDEX="rag-poc"
export BLOB_CONTAINER_URI="$(tf blob_container_uri)"
echo "Environment set for $AZURE_SEARCH_ENDPOINT"
