# Fix for Release 9.0.0 Failure

## Problem Summary

The release workflow for version 9.0.0 failed with the following error:

```
error NU5019: File not found: '/home/runner/work/ColumnEncryptor/ColumnEncryptor/src/ColumnEncryptor/README.md'
```

## Root Cause

The git tag `9.0.0` was created pointing to commit `72a6ad5` (Merge pull request #6), which does NOT include the `src/ColumnEncryptor/README.md` file. However, the `.csproj` file references this README for NuGet packaging (line 27):

```xml
<None Include="README.md" Pack="true" PackagePath="\" />
```

The README.md file was added in a later commit `c1676b2` (Refactor README and project files for clarity and consistency), which came AFTER the 9.0.0 tag was created.

## Timeline of Events

1. **Commit 72a6ad5** - Merged PR #6, created tag 9.0.0 → **NO README.md in src/ColumnEncryptor/**
2. **Commit c1676b2** - Added README.md files → **HAS README.md in src/ColumnEncryptor/**
3. Release workflow triggered for tag 9.0.0 → **FAILED** due to missing README.md

## Solution

Move the git tag `9.0.0` to point to commit `c1676b2` which includes the required README.md file.

### Steps to Fix (Manual)

Run these commands locally:

```bash
# Delete the local tag
git tag -d 9.0.0

# Create tag pointing to the correct commit
git tag 9.0.0 c1676b21d82b642619d6c7998303dd67554bd64e

# Force push the updated tag
git push --force origin 9.0.0
```

### Alternative: Delete and Recreate Release

1. Delete the existing 9.0.0 release on GitHub
2. Delete the 9.0.0 tag on GitHub
3. Create a new tag from commit `c1676b2` or later
4. Create a new release from the new tag

## Verification

After moving the tag, verify the README.md exists:

```bash
git ls-tree 9.0.0 src/ColumnEncryptor/ | grep README
```

Expected output:
```
100644 blob 7b0e84d7d47fcf3da43402f9e1c51be08d89cf04	src/ColumnEncryptor/README.md
```

## Prevention

To prevent this issue in the future:

1. Always ensure all required files (including README.md) are committed BEFORE creating release tags
2. Test the `dotnet pack` command locally before creating a release tag
3. Consider adding a pre-release validation step in the CI/CD pipeline

## Files Affected

- **Tag:** 9.0.0
- **Old commit:** 72a6ad5e93d8fe3bb903fd837716a76b39bdf6e9
- **New commit:** c1676b21d82b642619d6c7998303dd67554bd64e
- **Missing file:** src/ColumnEncryptor/README.md
