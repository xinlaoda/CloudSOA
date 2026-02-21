# ============================================================================
# CloudSOA Private Networking
# Creates VNet, subnets, and Private DNS zones for fully private deployment.
# Not deployed by default — enable with: var.enable_private_networking = true
# ============================================================================

variable "enable_private_networking" {
  description = "Enable VNet, private endpoints, and Private Link Service. Default: false (public deployment)."
  type        = bool
  default     = false
}

variable "vnet_address_space" {
  description = "VNet address space"
  type        = string
  default     = "10.100.0.0/16"
}

variable "aks_subnet_cidr" {
  description = "AKS node subnet CIDR"
  type        = string
  default     = "10.100.0.0/20"
}

variable "private_endpoint_subnet_cidr" {
  description = "Subnet for Azure Private Endpoints"
  type        = string
  default     = "10.100.16.0/24"
}

variable "private_link_service_subnet_cidr" {
  description = "Subnet for Private Link Service (CloudSOA frontend)"
  type        = string
  default     = "10.100.17.0/24"
}

# -------------------------------------------------------------------
# VNet & Subnets
# -------------------------------------------------------------------

resource "azurerm_virtual_network" "vnet" {
  count               = var.enable_private_networking ? 1 : 0
  name                = "${var.prefix}-vnet"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  address_space       = [var.vnet_address_space]
  tags                = var.tags
}

resource "azurerm_subnet" "aks" {
  count                = var.enable_private_networking ? 1 : 0
  name                 = "aks-subnet"
  resource_group_name  = azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.vnet[0].name
  address_prefixes     = [var.aks_subnet_cidr]
}

resource "azurerm_subnet" "private_endpoints" {
  count                = var.enable_private_networking ? 1 : 0
  name                 = "private-endpoints"
  resource_group_name  = azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.vnet[0].name
  address_prefixes     = [var.private_endpoint_subnet_cidr]
}

resource "azurerm_subnet" "private_link_service" {
  count                                          = var.enable_private_networking ? 1 : 0
  name                                           = "private-link-service"
  resource_group_name                            = azurerm_resource_group.main.name
  virtual_network_name                           = azurerm_virtual_network.vnet[0].name
  address_prefixes                               = [var.private_link_service_subnet_cidr]
  private_link_service_network_policies_enabled  = false
}

# -------------------------------------------------------------------
# Private DNS Zones (for private endpoint resolution)
# -------------------------------------------------------------------

locals {
  private_dns_zones = var.enable_private_networking ? {
    redis    = "privatelink.redis.cache.windows.net"
    cosmos   = "privatelink.documents.azure.com"
    blob     = "privatelink.blob.core.windows.net"
    acr      = "privatelink.azurecr.io"
    servicebus = "privatelink.servicebus.windows.net"
  } : {}
}

resource "azurerm_private_dns_zone" "zones" {
  for_each            = local.private_dns_zones
  name                = each.value
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "links" {
  for_each              = local.private_dns_zones
  name                  = "${each.key}-dns-link"
  resource_group_name   = azurerm_resource_group.main.name
  private_dns_zone_name = azurerm_private_dns_zone.zones[each.key].name
  virtual_network_id    = azurerm_virtual_network.vnet[0].id
}

# -------------------------------------------------------------------
# Outputs
# -------------------------------------------------------------------

output "vnet_id" {
  value = var.enable_private_networking ? azurerm_virtual_network.vnet[0].id : null
}

output "aks_subnet_id" {
  value = var.enable_private_networking ? azurerm_subnet.aks[0].id : null
}

output "private_endpoint_subnet_id" {
  value = var.enable_private_networking ? azurerm_subnet.private_endpoints[0].id : null
}

output "private_link_service_subnet_id" {
  value = var.enable_private_networking ? azurerm_subnet.private_link_service[0].id : null
}
