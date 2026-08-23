<#
.SYNOPSIS
    Compiles the sample MIDlet into a runnable suite JAR.

.DESCRIPTION
    The sample is compiled against the signature-only MIDP stubs in tests/fixtures/midp-stubs,
    which stand in for the long-discontinued Wireless Toolkit. The stubs are never packaged:
    the emulator supplies the real implementations of those classes at run time, so a JAR
    containing them would shadow the runtime library and fail.

    Requires a JDK on PATH, or JAVA_HOME set. Nothing else in this repository needs one --
    the emulator itself has no Java dependency.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\samples\dodger')
)

$ErrorActionPreference = 'Stop'

function Resolve-JavaTool([string] $name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    if ($env:JAVA_HOME) {
        $candidate = Join-Path $env:JAVA_HOME "bin\$name.exe"
        if (Test-Path $candidate) { return $candidate }
    }

    throw "$name was not found. Install a JDK, or set JAVA_HOME."
}

$javac = Resolve-JavaTool 'javac'
$jar = Resolve-JavaTool 'jar'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$stubs = Join-Path $repoRoot 'tests\fixtures\midp-stubs'
$source = Join-Path $repoRoot 'samples\dodger\src'
$build = Join-Path $repoRoot 'samples\dodger\build'
$jarPath = Join-Path (Resolve-Path $OutputDirectory) 'Dodger.jar'

if (Test-Path $build) { Remove-Item $build -Recurse -Force }
New-Item -ItemType Directory -Path $build -Force | Out-Null

Write-Host 'Compiling the sample against the MIDP stubs...'

$stubSources = Get-ChildItem -Path $stubs -Filter *.java -Recurse | ForEach-Object { $_.FullName }
$gameSources = Get-ChildItem -Path $source -Filter *.java -Recurse | ForEach-Object { $_.FullName }

# --release 8 keeps the bytecode inside what CLDC-era code looks like: no invokedynamic, and
# string concatenation compiled to StringBuilder rather than to a bootstrap method.
& $javac --release 8 -nowarn -encoding UTF-8 -d $build @($stubSources + $gameSources)
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }

# Drop the stub classes; only the game itself belongs in the suite.
Remove-Item (Join-Path $build 'javax') -Recurse -Force -ErrorAction SilentlyContinue

$manifest = Join-Path $build 'MANIFEST.MF'
@'
Manifest-Version: 1.0
MIDlet-Name: Dodger
MIDlet-Version: 1.0.0
MIDlet-Vendor: JMobile
MIDlet-Description: A sample MIDlet that exercises the emulator end to end.
MicroEdition-Configuration: CLDC-1.1
MicroEdition-Profile: MIDP-2.0
MIDlet-1: Dodger, , Dodger
'@ | Out-File -FilePath $manifest -Encoding ascii

if (Test-Path $jarPath) { Remove-Item $jarPath -Force }

Write-Host 'Packaging the suite...'
& $jar --create --file $jarPath --manifest $manifest -C $build .
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }

Remove-Item $build -Recurse -Force

Write-Host ''
Write-Host "Built $jarPath" -ForegroundColor Green
Write-Host 'Run it with:  dotnet run --project src/JMobile.App -- "' -NoNewline
Write-Host $jarPath -NoNewline
Write-Host '"'
