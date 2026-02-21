resource "azurerm_servicebus_namespace" "sb" {
  name                = "${var.prefix}-servicebus"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                           = "Standard"
  public_network_access_enabled = var.enable_private_networking ? false : true
  tags                          = var.tags
}

resource "azurerm_servicebus_queue" "requests" {
  name         = "soa-requests"
  namespace_id = azurerm_servicebus_namespace.sb.id

  max_delivery_count     = 10
  lock_duration          = "PT5M"
  max_size_in_megabytes  = 5120
  dead_lettering_on_message_expiration = true
  default_message_ttl    = "P1D"
}

resource "azurerm_servicebus_queue" "responses" {
  name         = "soa-responses"
  namespace_id = azurerm_servicebus_namespace.sb.id

  max_delivery_count     = 10
  lock_duration          = "PT5M"
  max_size_in_megabytes  = 5120
  dead_lettering_on_message_expiration = true
  default_message_ttl    = "P1D"
}
