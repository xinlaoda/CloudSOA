# Contributing to CloudSOA

Thank you for your interest in contributing! Here's how you can help.

## Development Setup

```bash
# Clone the repo
git clone https://github.com/xinlaoda/CloudSOA.git
cd CloudSOA

# Install dev environment
./scripts/setup-dev.sh

# Or manually:
dotnet restore
dotnet build
dotnet test --filter "Category!=Integration"
```

## Branch Strategy

- `main` — Stable release branch
- `develop` — Development branch
- `feature/*` — Feature branches (PR to develop)
- `fix/*` — Bug fix branches

## Pull Request Process

1. Fork the repository
2. Create a feature branch from `develop`
3. Make your changes with tests
4. Ensure all tests pass: `dotnet test`
5. Run the smoke test: `./scripts/smoke-test.sh`
6. Submit a PR to `develop`

## Coding Standards

- Follow existing code style (`.editorconfig` enforced)
- Write unit tests for new features
- Keep PRs focused and small
- Update documentation for API changes

## Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add durable session support
fix: handle Redis connection timeout
docs: update deployment guide
test: add dispatcher retry tests
refactor: extract queue interface
```

## Reporting Issues

Use GitHub Issues with these labels:
- `bug` — Something isn't working
- `enhancement` — New feature request
- `question` — Usage questions
