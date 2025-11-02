# NuGet Publishing Setup

This repository automatically publishes the `mostlylucid.mockllmapi` NuGet package when you create a version tag.

## Setup Instructions

### 1. Create NuGet API Key

1. Go to [NuGet.org](https://www.nuget.org/)
2. Sign in with your account
3. Click your username → **API Keys**
4. Click **Create**
5. Set the following:
   - **Key Name**: `mostlylucid.mockllmapi GitHub Actions`
   - **Select Scopes**: `Push new packages and package versions`
   - **Select Packages**: `mostlylucid.mockllmapi` (or leave as `*` for all)
   - **Glob Pattern**: `mostlylucid.mockllmapi*`
6. Click **Create**
7. **Copy the API key** (you won't see it again!)

### 2. Add Secret to GitHub

1. Go to your GitHub repository
2. Navigate to **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Set:
   - **Name**: `NUGET_API_KEY`
   - **Value**: Paste the API key from step 1
5. Click **Add secret**

## Publishing a New Version

### Option 1: Create a Git Tag (Recommended)

```bash
# Update version in mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj
# Then create and push a tag:
git tag v1.0.1
git push origin v1.0.1
```

The GitHub Action will automatically:
- Build the project
- Run all tests
- Pack the NuGet package with the version from the tag
- Publish to NuGet.org

### Option 2: Manual Trigger

1. Go to **Actions** tab in GitHub
2. Select **Publish to NuGet** workflow
3. Click **Run workflow**
4. Optionally specify a version number
5. Click **Run workflow**

## Workflow Triggers

The publish workflow runs when:
- You push a tag matching `v*.*.*` (e.g., `v1.0.0`, `v2.1.3`)
- You manually trigger it from the GitHub Actions UI

The build/test workflow runs on:
- Every push to `master`, `main`, or `develop` branches
- Every pull request to those branches

## Versioning

The package version comes from:
1. The git tag (if triggered by tag): `v1.2.3` → version `1.2.3`
2. Manual input (if triggered manually and version specified)
3. The version in `.csproj` file (fallback)

## Troubleshooting

**"Package already exists"**
- The version you're trying to publish already exists on NuGet
- Increment the version number in the tag
- The workflow uses `--skip-duplicate` flag to avoid errors

**"API key is invalid"**
- Check the secret is named exactly `NUGET_API_KEY`
- Verify the API key hasn't expired
- Regenerate the key on NuGet.org if needed

**Tests are failing**
- The workflow won't publish if tests fail
- Check the test results in the Actions tab
- Fix the tests and create a new tag

## Requirements

- .NET 8.0 SDK or later for building
- The package supports .NET 8.0 and .NET 9.0 (multi-targeted)

## CI/CD Status

[![Build and Test](https://github.com/mostlylucid/mostlylucid.mockllmapi/actions/workflows/build-test.yml/badge.svg)](https://github.com/mostlylucid/mostlylucid.mockllmapi/actions/workflows/build-test.yml)
[![Publish to NuGet](https://github.com/mostlylucid/mostlylucid.mockllmapi/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/mostlylucid/mostlylucid.mockllmapi/actions/workflows/publish-nuget.yml)

(Update the URLs above with your actual GitHub repository path)
