# NuGet Publishing Setup

This repository automatically publishes the `mostlylucid.mockllmapi` NuGet package when you create a version tag using **NuGet Trusted Publishers** (no API key required!).

## Setup Instructions

### Configure NuGet Trusted Publisher

This project uses NuGet's Trusted Publishers feature, which uses OpenID Connect (OIDC) for secure, keyless authentication from GitHub Actions.

**IMPORTANT**: For the first version (v1.0.0), you have two options:

#### Option A: Manual First Upload (Recommended)
1. Build the package locally: `cd mostlylucid.mockllmapi && dotnet pack -c Release`
2. Go to [NuGet.org](https://www.nuget.org/), sign in, and click **Upload**
3. Upload `mostlylucid.mockllmapi.1.0.0.nupkg` manually
4. Once uploaded, go to **Manage Package** → **Trusted Publishers**
5. Click **Add Trusted Publisher**
6. Set the following:
   - **Publisher Type**: GitHub Actions
   - **Owner**: `mostlylucid` (your GitHub username/org)
   - **Repository**: `mostlylucid.mockllmapi` (your repo name)
   - **Workflow**: `publish-nuget.yml`
   - **Environment**: (leave empty)
7. Click **Save**

Now all future versions can be published automatically via GitHub Actions!

#### Option B: Reserve Package ID (If Available)
1. Go to [NuGet.org](https://www.nuget.org/) and sign in
2. If NuGet allows package ID reservation, reserve `mostlylucid.mockllmapi`
3. Configure Trusted Publisher (steps 4-7 above)
4. Push tag and let GitHub Actions publish

**Note**: Requires .NET SDK 8.0.400+ for OIDC support (GitHub Actions has this by default).

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

**"401 Unauthorized" or "An API key must be provided"**
- This happens if the package doesn't exist yet on NuGet.org
- **Solution**: Manually upload v1.0.0 first (see Option A above), then configure Trusted Publisher
- After that, all subsequent versions will work automatically via GitHub Actions

**"Authentication failed" or "Forbidden"**
- Verify the Trusted Publisher is configured correctly on NuGet.org
- Check that the repository owner, name, and workflow file match exactly (case-sensitive!)
- Ensure the workflow has `id-token: write` permissions (already configured)
- Verify you're using .NET SDK 8.0.400+ (GitHub Actions runners should have this)

**"Tests are failing"**
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
