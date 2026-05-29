#!/usr/bin/env bash
#
# Emits the full build version as Major.Minor.Build.
#
# Major.Minor are read from the VERSION file at the repository root. That file
# is the manual control: edit it to bump the major or minor version.
#
# Build is the number of commits since the commit that last changed VERSION.
# So it is 0 on the commit that bumps Major.Minor and climbs by one for every
# commit after that. The build therefore resets when you bump Major.Minor and
# increases monotonically within a Major.Minor line, and it is reproducible
# from git history alone (no dependency on a CI run counter or release tags).
#
# Requires full git history: check out with actions/checkout fetch-depth: 0.
#
set -euo pipefail

major_minor=$(tr -d ' \t\r\n' < VERSION)
if [ -z "$major_minor" ]; then
    echo "VERSION file at the repository root is empty or missing." >&2
    exit 1
fi

version_anchor_commit=$(git log -1 --format=%H -- VERSION)
if [ -z "$version_anchor_commit" ]; then
    build=$(git rev-list --count HEAD)
else
    build=$(git rev-list --count "${version_anchor_commit}..HEAD")
fi

echo "${major_minor}.${build}"
