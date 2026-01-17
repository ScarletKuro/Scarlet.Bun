# AI Agent Resources

This directory contains resources for AI agents working on the Scarlet.Bun.MSBuild project.

## Files

### bun-llms-full.txt

Complete Bun documentation for Large Language Models, sourced from https://bun.com/llms-full.txt

This comprehensive documentation includes:
- Complete API reference for all Bun commands and features
- Usage examples and best practices
- Performance characteristics and optimization techniques
- Platform-specific behavior and compatibility notes
- Build, bundler, test runner, and runtime capabilities

**Usage:** AI agents should consult this documentation before:
- Implementing or modifying any Bun command execution
- Adding new Bun features or capabilities to the MSBuild task
- Troubleshooting Bun-related issues or errors
- Optimizing Bun command parameters or flags
- Updating integration tests that use Bun commands

## Updating Documentation

To update the Bun documentation to the latest version:

```bash
curl -L "https://bun.com/llms-full.txt" -o .github/agents/bun-llms-full.txt
```

This should be done periodically to ensure agents have access to the latest Bun features and documentation.
