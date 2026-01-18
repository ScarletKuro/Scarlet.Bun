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

### msbuild-llms-full.txt

**⚠️ CRITICAL: This documentation MUST be read before any MSBuild-related work.**

Comprehensive MSBuild documentation for Large Language Models, covering:
- MSBuild architecture, properties, items, targets, and tasks
- Custom task development best practices
- Dependency management (CopyLocalLockFileAssemblies, PrivateAssets)
- Build process and lifecycle
- Inline tasks with RoslynCodeTaskFactory
- Common patterns for multi-platform support, file system abstraction, incremental builds
- Troubleshooting and debugging techniques
- Testing custom MSBuild tasks

**This is an absolute requirement before:**
- Creating or modifying MSBuild tasks
- Adding package dependencies to tasks
- Working with .targets or .props files
- Debugging build issues
- Implementing custom build logic
- Packaging MSBuild tasks for NuGet

**Key concepts covered:**
- How MSBuild loads and executes custom tasks
- Why `CopyLocalLockFileAssemblies` is needed for task dependencies
- Proper error handling and logging in tasks
- Testing strategies with MockBuildEngine
- Dependency injection and IFileSystem abstraction

## Updating Documentation

### Bun Documentation

To update the Bun documentation to the latest version:

```bash
curl -L "https://bun.com/llms-full.txt" -o .github/agents/bun-llms-full.txt
```

### MSBuild Documentation

The MSBuild documentation is maintained manually. To update:

1. Review official Microsoft documentation for changes
2. Update msbuild-llms-full.txt with new information
3. Verify examples and code snippets are current
4. Update version number and last updated date

This should be done when:
- New MSBuild features are released
- Best practices change
- Issues are discovered in existing documentation
