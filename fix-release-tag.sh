#!/bin/bash

# Script to fix the 9.0.0 release tag
# This script moves the 9.0.0 tag to the correct commit that includes README.md
# 
# USAGE: 
#   Make sure the script is executable: chmod +x fix-release-tag.sh
#   Then run: ./fix-release-tag.sh

set -e

echo "=== Fixing 9.0.0 Release Tag ==="
echo ""

# The commit that includes the README.md file in src/ColumnEncryptor/
# This commit is "Refactor README and project files for clarity and consistency"
# which added README.md files to src/ColumnEncryptor/ and tests/ColumnEncryptor.Tests/
# Verification: git ls-tree c1676b2 src/ColumnEncryptor/ | grep README.md
CORRECT_COMMIT="c1676b21d82b642619d6c7998303dd67554bd64e"
TAG_NAME="9.0.0"

echo "Validating commit $CORRECT_COMMIT exists..."
if ! git rev-parse --verify "$CORRECT_COMMIT" >/dev/null 2>&1; then
    echo "✗ ERROR: Commit $CORRECT_COMMIT not found in repository"
    echo "  Please fetch the latest commits or check the commit hash"
    exit 1
fi
echo "✓ Commit validated"
echo ""

echo "Current tag information:"
git show-ref --tags | grep "$TAG_NAME" || echo "Tag not found locally"
echo ""

echo "Deleting local tag '$TAG_NAME' if it exists..."
# Check if tag exists before trying to delete it
if git tag -l | grep -q "^$TAG_NAME$"; then
    git tag -d "$TAG_NAME"
    echo "  Local tag deleted successfully"
else
    echo "  Tag doesn't exist locally (this is okay)"
fi
echo ""

echo "Creating new tag '$TAG_NAME' pointing to commit $CORRECT_COMMIT..."
git tag "$TAG_NAME" "$CORRECT_COMMIT"
echo ""

echo "Verifying README.md exists in the new tag..."
if git ls-tree "$TAG_NAME" src/ColumnEncryptor/ | grep -q "README.md"; then
    echo "✓ README.md found in tag $TAG_NAME"
    git ls-tree "$TAG_NAME" src/ColumnEncryptor/ | grep "README.md"
else
    echo "✗ ERROR: README.md NOT found in tag $TAG_NAME"
    exit 1
fi
echo ""

echo "Tag has been updated locally. To push to remote, run:"
echo "  git push --force origin $TAG_NAME"
echo ""
echo "WARNING: This will overwrite the existing tag on the remote repository."
echo "Make sure you have the necessary permissions before running the push command."
