resource "azurerm_storage_account" "blob" {
  name                     = "${replace(var.prefix, "-", "")}blob"
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags                     = var.tags
}

resource "azurerm_storage_container" "service_packages" {
  name                  = "service-packages"
  storage_account_name  = azurerm_storage_account.blob.name
  container_access_type = "private"
}
