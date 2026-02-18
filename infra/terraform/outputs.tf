output "aks_name" {
  value = azurerm_kubernetes_cluster.aks.name
}

output "aks_fqdn" {
  value = azurerm_kubernetes_cluster.aks.fqdn
}

output "acr_login_server" {
  value = azurerm_container_registry.acr.login_server
}

output "redis_hostname" {
  value = azurerm_redis_cache.redis.hostname
}

output "redis_primary_key" {
  value     = azurerm_redis_cache.redis.primary_access_key
  sensitive = true
}

output "servicebus_connection_string" {
  value     = azurerm_servicebus_namespace.sb.default_primary_connection_string
  sensitive = true
}

output "cosmosdb_endpoint" {
  value = azurerm_cosmosdb_account.cosmos.endpoint
}

output "cosmosdb_primary_key" {
  value     = azurerm_cosmosdb_account.cosmos.primary_key
  sensitive = true
}
