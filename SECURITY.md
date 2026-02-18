# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 0.1.x   | :white_check_mark: |

## Reporting a Vulnerability

If you discover a security vulnerability, please report it responsibly:

1. **Do NOT** open a public GitHub issue
2. Email: [security contact to be configured]
3. Or use GitHub's [private vulnerability reporting](https://github.com/xinlaoda/CloudSOA/security/advisories/new)

We will acknowledge receipt within 48 hours and provide a fix timeline.

## Security Considerations

- API Key authentication is available but optional in dev mode
- Redis connections should use TLS in production (Azure Redis enforces this)
- gRPC endpoints use HTTP/2 without TLS internally (use service mesh for mTLS)
- Secrets should be stored in Kubernetes Secrets or Azure Key Vault, never in code
