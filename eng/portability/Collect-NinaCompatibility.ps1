[CmdletBinding()]
param(
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string] $Revision = '4742afae0a09c79be772f23fcf163fb5852d2775',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/portability/nina'),
    [string[]] $EvidencePath,
    [string] $FixtureDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = 'isbeorn/nina'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$cache = Join-Path $output $Revision
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$statusPath = Join-Path $cache 'run-status.json'
$headers = @{ 'User-Agent' = 'Wpf-Portability-Inventory'; Accept = 'application/vnd.github+json' }

function Write-JsonFile($Value, [string] $Path) {
    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Read-Evidence([string] $Url, [string] $RelativePath, [string] $FixturePath) {
    $destination = Join-Path $cache $RelativePath
    $hashPath = "$destination.sha256"
    if (Test-Path -LiteralPath $destination) {
        if (!(Test-Path -LiteralPath $hashPath)) { throw "Cache hash missing: $destination" }
        $expected = (Get-Content -LiteralPath $hashPath -Raw).Trim()
        $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $expected) { throw "Cache SHA-256 mismatch: $destination" }
    } else {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        $temporary = "$destination.$([guid]::NewGuid().ToString('N')).download"
        try {
            if ($FixtureDirectory) {
                Copy-Item -LiteralPath (Join-Path $FixtureDirectory $FixturePath) -Destination $temporary
            } else {
                Invoke-WebRequest -Uri $Url -Headers $headers -OutFile $temporary -TimeoutSec 45
            }
            $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
            Move-Item -LiteralPath $temporary -Destination $destination
            Set-Content -LiteralPath $hashPath -Value $actual -Encoding ascii
        } finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
        }
    }
    return [pscustomobject]@{ Path = $destination; Sha256 = $actual; Url = $Url }
}

function Get-GitBlobId([byte[]] $Bytes) {
    $prefix = [Text.Encoding]::ASCII.GetBytes("blob $($Bytes.Length)`0")
    $sha = [Security.Cryptography.SHA1]::Create()
    try {
        $null = $sha.TransformBlock($prefix, 0, $prefix.Length, $prefix, 0)
        $null = $sha.TransformFinalBlock($Bytes, 0, $Bytes.Length)
        return ([BitConverter]::ToString($sha.Hash)).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

try {
    Write-JsonFile @{ Status = 'running'; Revision = $Revision; StartedUtc = [DateTime]::UtcNow.ToString('o') } $statusPath
    $sourceKind = if ($FixtureDirectory) { 'disposable-fixture' } else { 'github-https-get' }
    $contextPath = Join-Path $cache 'cache-context.json'
    if (Test-Path -LiteralPath $contextPath) {
        $context = Get-Content -LiteralPath $contextPath -Raw | ConvertFrom-Json
        if ($context.Source -cne $sourceKind -or $context.Repository -cne $repository -or $context.Revision -cne $Revision) {
            throw 'Cache source context mismatch. Use a separate output directory for fixture and public evidence.'
        }
    } else {
        Write-JsonFile @{ Source = $sourceKind; Repository = $repository; Revision = $Revision } $contextPath
    }
    $commitDownload = Read-Evidence "https://api.github.com/repos/$repository/git/commits/$Revision" 'metadata/commit.json' 'commit.json'
    $commit = Get-Content -LiteralPath $commitDownload.Path -Raw | ConvertFrom-Json
    if ($commit.sha -cne $Revision) { throw 'Commit API did not return the requested revision.' }
    $treeId = $commit.tree.sha
    if ($treeId -notmatch '^[0-9a-f]{40}$') { throw 'Invalid root tree ID.' }
    $treeDownload = Read-Evidence "https://api.github.com/repos/$repository/git/trees/${treeId}?recursive=1" 'metadata/tree.json' 'tree.json'
    $tree = Get-Content -LiteralPath $treeDownload.Path -Raw | ConvertFrom-Json
    if ($tree.sha -cne $treeId) { throw 'Tree API did not return the pinned commit root tree.' }
    if ($tree.truncated -ne $false) { throw 'Recursive tree is truncated or lacks an explicit false truncation flag.' }
    $blobs = @{}
    foreach ($entry in $tree.tree) {
        if ($entry.type -eq 'blob') { $blobs[$entry.path] = $entry }
    }
    if (!$EvidencePath) {
        $EvidencePath = @($blobs.Keys | Where-Object { $_ -match '\.csproj$|(^|/)Directory\.(Build|Packages)\.(props|targets)$|(^|/)global\.json$' }) + @(
            'NINA/App.xaml', 'NINA/App.xaml.cs', 'NINA/MainWindow.xaml', 'NINA/MainWindow.xaml.cs',
            'NINA/AppJumpListExtensions.cs', 'NINA/ViewModel/ProfileSelectVM.cs',
            'NINA/View/Imaging/OverView.xaml', 'NINA/ViewModel/DockManagerVM.cs',
            'NINA/Resources/Styles/AvalonDock.xaml', 'NINA/Resources/Styles/Markdown.xaml',
            'NINA/View/HistogramView.xaml', 'NINA/View/AutoFocusChart.xaml',
            'NINA/View/FramingAssistantView.xaml', 'NINA/View/Plugins/PluginsView.xaml',
            'NINA/View/Sequencer/AdvancedSequencer/AdvancedSequencerView.xaml',
            'NINA/View/Sequencer/SimpleSequencer/SimpleDSOContainerView.xaml',
            'NINA/Utility/PaneTemplateSelector.cs', 'NINA.Plugin/PluginLoader.cs',
            'NINA.WPF.Base/Utility/SharedResourceDictionary.cs',
            'NINA.WPF.Base/Resources/StaticResources/Brushes.xaml',
            'NINA.WPF.Base/View/ImageView.xaml', 'NINA.WPF.Base/View/ImageView.xaml.cs',
            'NINA.WPF.Base/View/Browser.xaml', 'NINA.WPF.Base/View/Browser.xaml.cs',
            'NINA.WPF.Base/SkySurvey/MosaicSkySurvey.cs',
            'NINA.WPF.Base/ViewModel/Equipment/DeviceChooserVM.cs',
            'NINA.CustomControlLibrary/Themes/Generic.xaml', 'NINA.CustomControlLibrary/StepperControl.cs',
            'NINA.CustomControlLibrary/OutlinedTextBlock.cs',
            'NINA.Core/Utility/Extensions/WPFExtensions.cs',
            'NINA.Core/Utility/Notification/SameScreenAsApplicationWorkAreaProvider.cs',
            'NINA.Core/Utility/AsyncObservableCollection.cs', 'NINA.Core/Utility/Logger.cs',
            'NINA.Image/ImageAnalysis/ImageUtility.cs', 'NINA.Image/ImageData/BaseImageData.cs',
            'NINA.Image/Interfaces/IRenderedImage.cs'
        )
    }
    $selected = @($EvidencePath | Sort-Object -Unique)
    if ($selected.Count -gt 100) { throw 'Evidence selection exceeds the 100-file bound.' }
    $files = @()
    $packages = @()
    $properties = @()
    $usageMatches = @()
    $patterns = [ordered]@{
        NativeInterop = 'DllImport|LibraryImport|HwndSource|HwndHost|WindowInteropHelper|WindowsFormsHost|JumpList|JumpTask|SystemEvents'
        Dispatch = 'DispatcherFrame|Dispatcher\.PushFrame|DispatcherSynchronizationContext|SetApartmentState|DispatcherTimer|new Thread'
        Imaging = 'BitmapSource|WriteableBitmap|RenderTargetBitmap|DrawingVisual|PixelFormats\.|CopyPixels|\.Freeze\(|BitmapDecoder|BitmapEncoder'
        ThirdPartyUi = 'AvalonDock|OxyPlot|MdXaml|MarkdownScrollViewer|WebView2|AvalonEdit|Xceed|xctk:'
        XamlResources = 'SharedResourceDictionary|MergedDictionaries|component/|LoadComponent|DataTemplateImports|SelectTemplate|DefaultStyleKeyProperty|OnApplyTemplate'
        Rendering = 'ShaderEffect|PixelShader|D3DImage|Media3D|OnRender\(|FormattedText|StreamGeometry'
    }
    foreach ($path in $selected) {
        if ($path -match '(^|/)\.\.?(/|$)|\\|^/|:') { throw "Unsafe evidence path: $path" }
        if (!$blobs.ContainsKey($path)) { throw "Required evidence missing from pinned tree: $path" }
        $entry = $blobs[$path]
        if ($entry.size -gt 1048576) { throw "Evidence file exceeds 1 MiB bound: $path" }
        $escapedPath = (($path.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/')
        $download = Read-Evidence "https://raw.githubusercontent.com/$repository/$Revision/$escapedPath" "source/$path" "source/$path"
        $bytes = [IO.File]::ReadAllBytes($download.Path)
        $blobId = Get-GitBlobId $bytes
        if ($blobId -cne $entry.sha -or $bytes.Length -ne $entry.size) { throw "Pinned blob ID or size mismatch: $path" }
        $files += [pscustomobject]@{ Path = $path; BlobId = $blobId; Sha256 = $download.Sha256; Bytes = $bytes.Length; Url = $download.Url }
        $content = [IO.File]::ReadAllText($download.Path)
        if ($path -match '\.(csproj|props|targets)$') {
            [xml] $xml = $content
            foreach ($node in $xml.SelectNodes('//*[local-name()="PackageReference"]')) {
                $version = $node.GetAttribute('Version')
                if (!$version) {
                    $child = $node.SelectSingleNode('*[local-name()="Version"]')
                    if ($null -ne $child) { $version = $child.InnerText }
                }
                $packages += [pscustomobject]@{ Project = $path; Name = $node.GetAttribute('Include'); Version = $version }
            }
            foreach ($node in $xml.SelectNodes('//*[local-name()="TargetFramework" or local-name()="TargetFrameworks" or local-name()="TargetFrameworkVersion" or local-name()="UseWPF" or local-name()="UseWindowsForms" or local-name()="RuntimeIdentifier" or local-name()="EnableWindowsTargeting" or local-name()="ImportWindowsDesktopTargets"]')) {
                $properties += [pscustomobject]@{ Project = $path; Name = $node.LocalName; Value = $node.InnerText }
            }
        }
        $lines = $content -split '\r?\n'
        for ($i = 0; $i -lt $lines.Length; $i++) {
            foreach ($category in $patterns.Keys) {
                if ($lines[$i] -match $patterns[$category]) {
                    $usageMatches += [pscustomobject]@{ Category = $category; Path = $path; Line = $i + 1; Text = $lines[$i].Trim() }
                }
            }
        }
    }
    $inventory = [ordered]@{
        SchemaVersion = 1; Status = 'complete'; Repository = $repository; Revision = $Revision
        Source = $sourceKind
        CollectedUtc = [DateTime]::UtcNow.ToString('o'); RootTreeId = $treeId; TreeTruncated = $tree.truncated
        CommitResponseSha256 = $commitDownload.Sha256; TreeResponseSha256 = $treeDownload.Sha256
        TreeBlobCount = $blobs.Count; TreeXamlCount = @($blobs.Keys | Where-Object { $_ -match '\.xaml$' }).Count
        ShaderPathsInTree = @($blobs.Keys | Where-Object { $_ -match '\.(ps|fx|hlsl)$|Shader' } | Sort-Object)
        Files = $files; FrameworkProperties = $properties; Packages = $packages; UsageMatches = $usageMatches
        Limitations = @('Static bounded source inventory, not runtime or binary compatibility certification.', 'Only listed files were content-scanned. No transitive package source or native binaries downloaded.', 'Metadata cache uses SHA-256 sidecars; source bytes additionally match pinned tree Git blob IDs.')
    }
    Write-JsonFile $inventory (Join-Path $cache 'inventory.json')
    Write-JsonFile @{ Status = 'complete'; Revision = $Revision; Files = $files.Count; CompletedUtc = [DateTime]::UtcNow.ToString('o') } $statusPath
    Write-Output "NINA ${Revision}: $($files.Count) verified files, tree $treeId, truncated=$($tree.truncated)."
    Write-Output (Join-Path $cache 'inventory.json')
} catch {
    Write-JsonFile @{ Status = 'failed'; Revision = $Revision; Error = $_.Exception.Message; FailedUtc = [DateTime]::UtcNow.ToString('o') } $statusPath
    throw
}
