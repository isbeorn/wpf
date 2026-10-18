[CmdletBinding()]
param(
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..')),
    [string]$UnicodeInput
)
$ErrorActionPreference = 'Stop'
if (-not $UnicodeInput) { $UnicodeInput = Join-Path $RepositoryRoot 'artifacts/portability/text-bidi/unicode/BidiCharacterTest.txt' }
$outputDirectory = Join-Path $RepositoryRoot 'artifacts/portability/text-bidi/full-conformance'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
if (-not (Test-Path -LiteralPath $UnicodeInput)) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($UnicodeInput)) | Out-Null
    Invoke-WebRequest 'https://www.unicode.org/Public/17.0.0/ucd/BidiCharacterTest.txt' -OutFile $UnicodeInput
}
$dependency = Join-Path $RepositoryRoot 'src/Portability/Dependencies/TextUnicode'
$manifest = Get-Content -LiteralPath (Join-Path $dependency 'manifest.json') -Raw | ConvertFrom-Json
$expectedInputHash = 'A3E6E905AB5AFBE318A96DF5401D0372A04CD73EF139AB5E3CF0AE241C255488'
$inputHash = (Get-FileHash -LiteralPath $UnicodeInput -Algorithm SHA256).Hash
if ($inputHash -ne $expectedInputHash) { throw 'Unicode 17 conformance input hash mismatch.' }
$parts = [Collections.Generic.List[string]]::new()
$sourceHashes = [Collections.Generic.List[object]]::new()
foreach ($variant in @('PatRaw', 'PatFixed')) {
    foreach ($record in ($manifest.files | Where-Object path -Like '*.cs')) {
        $path = Join-Path (Join-Path $dependency 'Original') $record.path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($hash -ne $record.sha256) { throw "Pinned original changed: $path" }
        if ($variant -eq 'PatRaw') { $sourceHashes.Add([ordered]@{ path = $record.path; sha256 = $hash }) }
        $source = [IO.File]::ReadAllText($path)
        $namespaces = [regex]'Avalonia\.(?:Media\.TextFormatting(?:\.Unicode)?|Utilities)'
        $publicTypes = [regex]'(?m)^    public (?=(?:(?:readonly|ref|partial|sealed|static) )*(?:class|struct|record|enum))'
        if ($namespaces.Matches($source).Count -ne $record.namespaces -or $publicTypes.Matches($source).Count -ne $record.publicTypes) {
            throw "Adaptation shape changed: $path"
        }
        $source = $namespaces.Replace($source, {
            param($match)
            $match.Value.Replace('Avalonia.Media.TextFormatting', 'MS.Internal.Portability.Text').Replace('Avalonia.Utilities', 'MS.Internal.Portability.Text.Utilities')
        })
        $source = $publicTypes.Replace($source, '    internal ')
        if ($record.qualifiedBuilders -ne 0) {
            if ([regex]::Matches($source, '\bArrayBuilder<').Count -ne $record.qualifiedBuilders) { throw "Utility adaptation changed: $path" }
            $source = [regex]::Replace($source, '\bArrayBuilder<', 'MS.Internal.Portability.Text.Utilities.ArrayBuilder<')
        }
        $source = "#nullable enable annotations`n" + $source
        if ([regex]::IsMatch($source, '(?m)^namespace [\w.]+;')) {
            $source = [regex]::Replace($source, '(?m)^(namespace [\w.]+);', '$1 {') + "`n}"
        }
        if ([IO.Path]::GetFileName($path) -eq 'BiDiAlgorithm.cs') {
            $assignment = '_runOriginalClasses[i] = direction;'
            if ([regex]::Matches($source, [regex]::Escape($assignment)).Count -ne 2) { throw 'Expected exactly two original assignments.' }
            if ($variant -eq 'PatFixed') { $source = $source.Replace($assignment, '_runResolvedClasses[i] = direction;') }
        }
        $parts.Add("namespace $variant {`n$source`n}")
    }
    $wrapper = @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MS.Internal.Portability.Text.Unicode;
public sealed class ProbeResult {
 public int Cases { get; set; }
 public int ComparedLevels { get; set; }
 public int FailureCount { get; set; }
 public int MutatedInputClasses { get; set; }
 public List<string> Failures { get; } = new List<string>();
 public List<string> NeighborChecks { get; } = new List<string>();
}
public static class BracketProbe {
 public static ProbeResult Run(string file) {
  var result = new ProbeResult(); int line=0;
  foreach(var row in File.ReadLines(file)) {
   line++; if(string.IsNullOrWhiteSpace(row)||row[0]=='#')continue;
   var f=row.Split(';');
   var text=string.Concat(f[0].Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(x=>char.ConvertFromUtf32(Convert.ToInt32(x,16))));
   var data=new BidiData {ParagraphEmbeddingLevel=sbyte.Parse(f[1])};data.Append(text);
   var before=new BidiClass[data.Length];for(int i=0;i<before.Length;i++)before[i]=data.Classes[i];
   var algorithm=new BidiAlgorithm();algorithm.Process(data);
   var expected=f[3].Split(' ',StringSplitOptions.RemoveEmptyEntries);
   if (data.Length != expected.Length) throw new InvalidDataException("Scalar/expected-level count mismatch at line " + line);
   bool match=true;var actual=new int[expected.Length];
   for(int i=0;i<actual.Length;i++){
    actual[i]=algorithm.ResolvedLevels[i];
    if(expected[i]!="x") { result.ComparedLevels++; if(actual[i]!=int.Parse(expected[i]))match=false; }
    if(before[i]!=data.Classes[i])result.MutatedInputClasses++;
   }
   if(algorithm.ResolvedParagraphEmbeddingLevel!=int.Parse(f[2]))match=false;
   result.Cases++;
   if(!match)result.Failures.Add("line="+line+" expected="+f[3]+" actual="+string.Join(" ",actual));
   if(line>=85&&line<=116)result.NeighborChecks.Add("line="+line+" "+(match?"PASS":"FAIL")+" actual="+string.Join(" ",actual));
  }
  result.FailureCount=result.Failures.Count;
  return result;
 }
}
'@
    $parts.Add("namespace $variant {`n$wrapper`n}")
}
Add-Type -TypeDefinition ($parts -join "`n") -CompilerOptions '/unsafe' -WarningAction Stop
$original = [PatRaw.BracketProbe]::Run($UnicodeInput)
$corrected = [PatFixed.BracketProbe]::Run($UnicodeInput)
$result = [ordered]@{
    description = 'Independent original-versus-two-assignment N0 comparison. Sources compile in memory; production files are not modified.'
    scope = 'All official Unicode 17 BidiCharacterTest cases: resolved paragraph level and every non-x character level. Also verifies input-class preservation. Does not check visual ordering, rendering, line breaking or public WPF integration.'
    sourceCommit = $manifest.commit
    archiveSha256 = $manifest.archiveSha256
    unicodeInput = $UnicodeInput
    unicodeInputSha256 = $inputHash
    sourceHashes = @($sourceHashes)
    original = $original
    corrected = $corrected
    recordedAtUtc = [DateTime]::UtcNow.ToString('o')
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    command = 'pwsh -NoProfile -File src/Portability/Tests/TextIntegration/Verify-UnicodeBidi.ps1'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputDirectory 'results.json') -Encoding utf8
[pscustomobject]$result | Select-Object scope, original, corrected | ConvertTo-Json -Depth 6
if ($original.Cases -ne 91707 -or $original.FailureCount -ne 9 -or $original.MutatedInputClasses -ne 23 -or
    $corrected.Cases -ne 91707 -or $corrected.FailureCount -ne 0 -or $corrected.MutatedInputClasses -ne 0) {
    throw 'Independent probe did not reproduce the expected original/corrected comparison.'
}
