terraform {
  required_version = ">= 1.6"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
    random  = { source = "hashicorp/random", version = "~> 3.6" }
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}

variable "subscription_id" { type = string }

variable "location" {
  type        = string
  default     = "swedencentral" # pick a region where your subscription has Azure OpenAI quota
  description = "Azure region. Model availability and quota differ per region."
}

variable "prefix" {
  type    = string
  default = "ragpoc"
}

# Model names and versions change over time: check the Azure OpenAI model docs for your region.
variable "chat_model" {
  type    = string
  default = "gpt-4o-mini"
}
variable "chat_model_version" {
  type    = string
  default = "2024-07-18"
}
variable "embedding_model" {
  type    = string
  default = "text-embedding-3-small" # 1536 dimensions, matches EMBEDDING_DIMENSIONS
}
variable "embedding_model_version" {
  type    = string
  default = "1"
}

data "azurerm_client_config" "current" {}

resource "random_string" "suffix" {
  length  = 5
  special = false
  upper   = false
}

locals {
  name = "${var.prefix}-${random_string.suffix.result}"
}

resource "azurerm_resource_group" "rg" {
  name     = "rg-${local.name}"
  location = var.location
}

# ---- Storage: where the source documents live ----
resource "azurerm_storage_account" "docs" {
  name                            = "st${var.prefix}${random_string.suffix.result}"
  resource_group_name             = azurerm_resource_group.rg.name
  location                        = azurerm_resource_group.rg.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  allow_nested_items_to_be_public = false
  min_tls_version                 = "TLS1_2"
}

resource "azurerm_storage_container" "docs" {
  name                  = "docs"
  storage_account_id    = azurerm_storage_account.docs.id
  container_access_type = "private"
}

# ---- Azure OpenAI: one account, two model deployments ----
resource "azurerm_cognitive_account" "openai" {
  name                  = "oai-${local.name}"
  resource_group_name   = azurerm_resource_group.rg.name
  location              = azurerm_resource_group.rg.location
  kind                  = "OpenAI"
  sku_name              = "S0"
  custom_subdomain_name = "oai-${local.name}" # required for Entra ID (token) authentication
}

resource "azurerm_cognitive_deployment" "embedding" {
  name                 = "embedding"
  cognitive_account_id = azurerm_cognitive_account.openai.id
  model {
    format  = "OpenAI"
    name    = var.embedding_model
    version = var.embedding_model_version
  }
  sku {
    name     = "Standard"
    capacity = 20 # thousands of tokens per minute
  }
}

resource "azurerm_cognitive_deployment" "chat" {
  name                 = "chat"
  cognitive_account_id = azurerm_cognitive_account.openai.id
  model {
    format  = "OpenAI"
    name    = var.chat_model
    version = var.chat_model_version
  }
  sku {
    name     = "GlobalStandard"
    capacity = 20
  }
  depends_on = [azurerm_cognitive_deployment.embedding] # deployments on one account are created one at a time
}

# ---- Azure AI Search: the vector + keyword index ----
resource "azurerm_search_service" "search" {
  name                         = "srch-${local.name}"
  resource_group_name          = azurerm_resource_group.rg.name
  location                     = azurerm_resource_group.rg.location
  sku                          = "basic" # billed hourly while it exists: run `terraform destroy` when done
  local_authentication_enabled = false   # API keys disabled: Entra ID / RBAC only
}

# ---- RBAC: give YOUR signed-in identity least-privilege access (no keys anywhere) ----
resource "azurerm_role_assignment" "openai_user" {
  scope                = azurerm_cognitive_account.openai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "search_service" {
  scope                = azurerm_search_service.search.id
  role_definition_name = "Search Service Contributor" # create/update the index definition
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "search_data" {
  scope                = azurerm_search_service.search.id
  role_definition_name = "Search Index Data Contributor" # upload and query documents
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "blob" {
  scope                = azurerm_storage_account.docs.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

output "openai_endpoint" { value = azurerm_cognitive_account.openai.endpoint }
output "embedding_deployment" { value = azurerm_cognitive_deployment.embedding.name }
output "chat_deployment" { value = azurerm_cognitive_deployment.chat.name }
output "search_endpoint" { value = "https://${azurerm_search_service.search.name}.search.windows.net" }
output "blob_container_uri" { value = "${azurerm_storage_account.docs.primary_blob_endpoint}${azurerm_storage_container.docs.name}" }
output "storage_account" { value = azurerm_storage_account.docs.name }
