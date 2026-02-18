variable "prefix" {
  description = "Resource name prefix"
  type        = string
  default     = "cloudsoa"
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
  default     = "Standard_D4s_v3"
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
