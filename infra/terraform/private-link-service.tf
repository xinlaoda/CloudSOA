# ============================================================================
# Azure Private Link Service for CloudSOA
#
# Exposes CloudSOA Broker and Portal as Private Link Services, allowing
# clients in different VNets or subscriptions to create Private Endpoints
# to securely access CloudSOA without public IP addresses.
#
# Not deployed by default — enable with: var.enable_private_networking = true
# ============================================================================

variable "enable_private_link_service" {
  description = "Create Private Link Service for CloudSOA (requires enable_private_networking=true and internal LB deployed)"
  type        = bool
  default     = false
}

# -------------------------------------------------------------------
# Private Link Service — Broker
#
# Backed by the AKS internal Load Balancer for broker-service.
# Clients create a Private Endpoint pointing to this PLS to access
# the CloudSOA Broker API over private networking.
#
# Prerequisites:
#   1. Broker K8s Service must use type: LoadBalancer with
#      service.beta.kubernetes.io/azure-load-balancer-internal: "true"
#   2. The internal LB frontend IP must be obtained after deploying
#      the K8s Service. Set var.broker_internal_lb_frontend_ip_id.
# -------------------------------------------------------------------

variable "broker_internal_lb_frontend_ip_id" {
  description = "Azure resource ID of the Broker internal LoadBalancer frontend IP configuration. Obtain from: az network lb frontend-ip list ..."
  type        = string
  default     = ""
}

variable "portal_internal_lb_frontend_ip_id" {
  description = "Azure resource ID of the Portal internal LoadBalancer frontend IP configuration."
  type        = string
  default     = ""
}

resource "azurerm_private_link_service" "broker" {
  count               = var.enable_private_networking && var.enable_private_link_service && var.broker_internal_lb_frontend_ip_id != "" ? 1 : 0
  name                = "${var.prefix}-broker-pls"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags

  auto_approval_subscription_ids              = []
  visibility_subscription_ids                 = []
  load_balancer_frontend_ip_configuration_ids = [var.broker_internal_lb_frontend_ip_id]

  nat_ip_configuration {
    name      = "broker-nat"
    primary   = true
    subnet_id = azurerm_subnet.private_link_service[0].id
  }
}

resource "azurerm_private_link_service" "portal" {
  count               = var.enable_private_networking && var.enable_private_link_service && var.portal_internal_lb_frontend_ip_id != "" ? 1 : 0
  name                = "${var.prefix}-portal-pls"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = var.tags

  auto_approval_subscription_ids              = []
  visibility_subscription_ids                 = []
  load_balancer_frontend_ip_configuration_ids = [var.portal_internal_lb_frontend_ip_id]

  nat_ip_configuration {
    name      = "portal-nat"
    primary   = true
    subnet_id = azurerm_subnet.private_link_service[0].id
  }
}

# -------------------------------------------------------------------
# Outputs
# -------------------------------------------------------------------

output "broker_private_link_service_id" {
  description = "Resource ID of the Broker Private Link Service. Share with consumers to create Private Endpoints."
  value       = var.enable_private_networking && var.enable_private_link_service && var.broker_internal_lb_frontend_ip_id != "" ? azurerm_private_link_service.broker[0].id : null
}

output "broker_private_link_service_alias" {
  description = "Alias of the Broker Private Link Service. Consumers use this to create Private Endpoints across subscriptions."
  value       = var.enable_private_networking && var.enable_private_link_service && var.broker_internal_lb_frontend_ip_id != "" ? azurerm_private_link_service.broker[0].alias : null
}

output "portal_private_link_service_id" {
  description = "Resource ID of the Portal Private Link Service."
  value       = var.enable_private_networking && var.enable_private_link_service && var.portal_internal_lb_frontend_ip_id != "" ? azurerm_private_link_service.portal[0].id : null
}
