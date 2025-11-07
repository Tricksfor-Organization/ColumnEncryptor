#!/bin/bash

# Script to fix the 9.0.0 release tag
# This script moves the 9.0.0 tag to the correct commit that includes README.md

set -e

echo "=== Fixing 9.0.0 Release Tag ==="
echo ""

# The commit that includes the README.md file
CORRECT_COMMIT="c1676b21d82b642619d6c7998303dd67554bd64e"
TAG_NAME="9.0.0"

echo "Current tag information:"
git show-ref --tags | grep "$TAG_NAME" || echo "Tag not found locally"
echo ""

echo "Deleting local tag '$TAG_NAME' if it exists..."
git tag -d "$TAG_NAME" 2>/dev/null || echo "Tag doesn't exist locally"
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
