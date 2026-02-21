resource "azurerm_storage_account" "blob" {
  name                          = "${replace(var.prefix, "-", "")}blob"
  resource_group_name           = azurerm_resource_group.main.name
  location                      = azurerm_resource_group.main.location
  account_tier                  = "Standard"
  account_replication_type      = "LRS"
  allow_nested_items_to_be_public = false
  public_network_access_enabled   = var.enable_private_networking ? false : true
  tags                            = var.tags

  dynamic "network_rules" {
    for_each = var.enable_private_networking ? [1] : []
    content {
      default_action = "Deny"
      bypass         = ["AzureServices"]
    }
  }
}

resource "azurerm_storage_container" "service_packages" {
  name                  = "service-packages"
  storage_account_name  = azurerm_storage_account.blob.name
  container_access_type = "private"
}
