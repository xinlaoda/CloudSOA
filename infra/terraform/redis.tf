resource "azurerm_redis_cache" "redis" {
  name                = "${var.prefix}-redis"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  capacity            = var.redis_capacity
  family              = var.redis_sku == "Basic" || var.redis_sku == "Standard" ? "C" : "P"
  sku_name            = var.redis_sku
  enable_non_ssl_port = false
  minimum_tls_version           = "1.2"
  public_network_access_enabled = var.enable_private_networking ? false : true
  tags                          = var.tags

  redis_configuration {
    maxmemory_policy = "allkeys-lru"
  }
}
