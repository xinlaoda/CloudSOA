variable "prefix" {
  description = "Resource name prefix"
  type        = string
  default     = "cloudsoa"
}

variable "resource_group_name" {
  description = "Override resource group name (default: {prefix}-rg)"
  type        = string
  default     = ""
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "eastus"
}

variable "tags" {
  description = "Resource tags"
  type        = map(string)
  default = {
    project     = "CloudSOA"
    environment = "dev"
  }
}

variable "aks_node_count" {
  description = "Default node pool count"
  type        = number
  default     = 3
}

variable "aks_vm_size" {
  description = "AKS node VM size"
  type        = string
  default     = "Standard_D4_v2"
}

variable "aks_compute_vm_size" {
  description = "AKS Linux compute node pool VM size"
  type        = string
  default     = "Standard_D4_v2"
}

variable "aks_win_compute_vm_size" {
  description = "AKS Windows compute node pool VM size"
  type        = string
  default     = "Standard_D4_v2"
}

variable "redis_sku" {
  description = "Redis cache SKU"
  type        = string
  default     = "Standard"
}

variable "redis_capacity" {
  description = "Redis cache capacity (0-6)"
  type        = number
  default     = 1
}
