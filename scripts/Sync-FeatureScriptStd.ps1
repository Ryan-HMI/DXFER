param(
    [string] $RepositoryUrl = "https://github.com/javawizard/onshape-std-library-mirror.git",
    [string] $Branch = "without-versions",
    [string] $MirrorRoot = "artifacts/featurescript-std/onshape-std-library-mirror",
    [string] $ManifestPath = "artifacts/featurescript-std/manifest.json",
    [switch] $SkipPull
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")

function Resolve-RepoPath([string] $PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

$mirrorRootPath = Resolve-RepoPath $MirrorRoot
$manifestFilePath = Resolve-RepoPath $ManifestPath
$mirrorParent = Split-Path -Parent $mirrorRootPath

New-Item -ItemType Directory -Force -Path $mirrorParent | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $mirrorRootPath ".git"))) {
    if (Test-Path -LiteralPath $mirrorRootPath) {
        throw "Mirror root exists but is not a git checkout: $mirrorRootPath"
    }

    git clone --depth 1 --branch $Branch $RepositoryUrl $mirrorRootPath
}
elseif (-not $SkipPull) {
    git -C $mirrorRootPath fetch origin $Branch --depth 1
    git -C $mirrorRootPath checkout $Branch
    git -C $mirrorRootPath pull --ff-only origin $Branch
}

dotnet run --project (Join-Path $repoRoot "tools/DXFER.FeatureScriptStd/DXFER.FeatureScriptStd.csproj") -- `
    --source $mirrorRootPath `
    --output $manifestFilePath
