# ============================================================================
# Azure Private Endpoints for CloudSOA Backend Services
# Ensures all traffic between AKS and Azure PaaS stays on Microsoft backbone.
# Not deployed by default — enable with: var.enable_private_networking = true
# ============================================================================

# -------------------------------------------------------------------
# Redis Cache — Private Endpoint
# -------------------------------------------------------------------

resource "azurerm_private_endpoint" "redis" {
  count               = var.enable_private_networking ? 1 : 0
  name                = "${var.prefix}-redis-pe"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  subnet_id           = azurerm_subnet.private_endpoints[0].id
  tags                = var.tags

  private_service_connection {
    name                           = "redis-psc"
    private_connection_resource_id = azurerm_redis_cache.redis.id
    is_manual_connection           = false
    subresource_names              = ["redisCache"]
  }

  private_dns_zone_group {
    name                 = "redis-dns"
    private_dns_zone_ids = [azurerm_private_dns_zone.zones["redis"].id]
  }
}

# Disable public access on Redis when private networking is enabled
resource "azurerm_redis_firewall_rule" "deny_all" {
  count               = var.enable_private_networking ? 0 : 0
  # Note: Redis public_network_access is controlled below
  name                = "deny-all"
  redis_cache_name    = azurerm_redis_cache.redis.name
  resource_group_name = azurerm_resource_group.main.name
  start_ip            = "0.0.0.0"
  end_ip              = "0.0.0.0"
}

# -------------------------------------------------------------------
# Cosmos DB — Private Endpoint
# -------------------------------------------------------------------

resource "azurerm_private_endpoint" "cosmos" {
  count               = var.enable_private_networking ? 1 : 0
  name                = "${var.prefix}-cosmos-pe"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  subnet_id           = azurerm_subnet.private_endpoints[0].id
  tags                = var.tags

  private_service_connection {
    name                           = "cosmos-psc"
    private_connection_resource_id = azurerm_cosmosdb_account.cosmos.id
    is_manual_connection           = false
    subresource_names              = ["Sql"]
  }

  private_dns_zone_group {
    name                 = "cosmos-dns"
    private_dns_zone_ids = [azurerm_private_dns_zone.zones["cosmos"].id]
  }
}

# -------------------------------------------------------------------
# Blob Storage — Private Endpoint
# -------------------------------------------------------------------

resource "azurerm_private_endpoint" "blob" {
  count               = var.enable_private_networking ? 1 : 0
  name                = "${var.prefix}-blob-pe"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  subnet_id           = azurerm_subnet.private_endpoints[0].id
  tags                = var.tags

  private_service_connection {
    name                           = "blob-psc"
    private_connection_resource_id = azurerm_storage_account.blob.id
    is_manual_connection           = false
    subresource_names              = ["blob"]
  }

  private_dns_zone_group {
    name                 = "blob-dns"
    private_dns_zone_ids = [azurerm_private_dns_zone.zones["blob"].id]
  }
}

# -------------------------------------------------------------------
# ACR — Private Endpoint
# -------------------------------------------------------------------

resource "azurerm_private_endpoint" "acr" {
  count               = var.enable_private_networking ? 1 : 0
  name                = "${var.prefix}-acr-pe"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  subnet_id           = azurerm_subnet.private_endpoints[0].id
  tags                = var.tags

  private_service_connection {
    name                           = "acr-psc"
    private_connection_resource_id = azurerm_container_registry.acr.id
    is_manual_connection           = false
    subresource_names              = ["registry"]
  }

  private_dns_zone_group {
    name                 = "acr-dns"
    private_dns_zone_ids = [azurerm_private_dns_zone.zones["acr"].id]
  }
}

# -------------------------------------------------------------------
# Service Bus — Private Endpoint
# -------------------------------------------------------------------

resource "azurerm_private_endpoint" "servicebus" {
  count               = var.enable_private_networking ? 1 : 0
  name                = "${var.prefix}-sb-pe"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  subnet_id           = azurerm_subnet.private_endpoints[0].id
  tags                = var.tags

  private_service_connection {
    name                           = "servicebus-psc"
    private_connection_resource_id = azurerm_servicebus_namespace.sb.id
    is_manual_connection           = false
    subresource_names              = ["namespace"]
  }

  private_dns_zone_group {
    name                 = "servicebus-dns"
    private_dns_zone_ids = [azurerm_private_dns_zone.zones["servicebus"].id]
  }
}

# -------------------------------------------------------------------
# Disable public network access when private networking is enabled
# (requires updating existing resources — use lifecycle to avoid drift)
# -------------------------------------------------------------------

# Note: To disable public access, add these attributes to existing resources:
#   azurerm_redis_cache.redis:      public_network_access_enabled = !var.enable_private_networking
#   azurerm_cosmosdb_account.cosmos: public_network_access_enabled = !var.enable_private_networking
#   azurerm_storage_account.blob:   public_network_access_enabled = !var.enable_private_networking ? "Enabled" : "Disabled"
#   azurerm_container_registry.acr: public_network_access_enabled = !var.enable_private_networking
#   azurerm_servicebus_namespace.sb: public_network_access_enabled = !var.enable_private_networking
#
# These are documented here rather than applied directly to avoid breaking
# the existing resources. Apply manually or add to the respective .tf files
# when enabling private networking.
