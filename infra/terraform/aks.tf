resource "azurerm_kubernetes_cluster" "aks" {
  name                = "${var.prefix}-aks"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  dns_prefix          = var.prefix
  tags                = var.tags

  default_node_pool {
    name       = "system"
    node_count = var.aks_node_count
    vm_size    = var.aks_vm_size
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin = "azure"
    network_policy = "calico"
  }
}

resource "azurerm_kubernetes_cluster_node_pool" "compute" {
  name                  = "compute"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.aks.id
  vm_size               = var.aks_compute_vm_size
  min_count             = 0
  max_count             = 50
  enable_auto_scaling   = true
  tags                  = var.tags

  node_labels = {
    "cloudsoa/role" = "compute"
  }

  node_taints = [
    "cloudsoa/role=compute:NoSchedule"
  ]
}

resource "azurerm_kubernetes_cluster_node_pool" "wincompute" {
  name                  = "wincom"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.aks.id
  vm_size               = var.aks_win_compute_vm_size
  os_type               = "Windows"
  min_count             = 0
  max_count             = 20
  enable_auto_scaling   = true
  tags                  = var.tags

  node_labels = {
    "cloudsoa/role" = "compute-windows"
  }

  node_taints = [
    "cloudsoa/role=compute-windows:NoSchedule"
  ]
}

resource "azurerm_container_registry" "acr" {
  name                = "${replace(var.prefix, "-", "")}acr"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Standard"
  admin_enabled       = true
  tags                = var.tags
}
