$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

function Find-AndroidSdk {
    foreach ($p in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk")) {
        if ($p -and [IO.Directory]::Exists("$p\build-tools")) { return $p }
    }
    throw "Android SDK not found. Install Android Studio or the command-line tools and set ANDROID_HOME."
}

function Find-BuildTools {
    $sdk = Find-AndroidSdk
    $dir = Get-ChildItem "$sdk\build-tools" -Directory |
        Where-Object { Test-Path "$($_.FullName)\aapt2.exe" } |
        Sort-Object { [version]($_.Name -replace '[^\d.].*$', '') } -Descending |
        Select-Object -First 1
    if (-not $dir) { throw "No build-tools with aapt2.exe in $sdk\build-tools" }
    return $dir.FullName
}

function Find-AndroidJar {
    $sdk = Find-AndroidSdk
    $jar = Get-ChildItem "$sdk\platforms" -Directory |
        Where-Object { Test-Path "$($_.FullName)\android.jar" } |
        Sort-Object { [int]($_.Name -replace '\D', '') } -Descending |
        Select-Object -First 1
    if (-not $jar) { throw "No platform android.jar in $sdk\platforms" }
    return "$($jar.FullName)\android.jar"
}

# apksigner from recent build-tools needs Java 11+; the java on PATH is often 1.8.
function Find-Java {
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += "$env:JAVA_HOME\bin\java.exe" }
    foreach ($root in @("$env:ProgramFiles\Microsoft", "$env:ProgramFiles\Eclipse Adoptium", "$env:ProgramFiles\Java", "$env:ProgramFiles\Android\Android Studio")) {
        if (Test-Path $root) {
            $candidates += Get-ChildItem $root -Recurse -Filter java.exe -Depth 3 -ErrorAction SilentlyContinue | ForEach-Object FullName
        }
    }
    foreach ($c in $candidates) {
        if (-not (Test-Path $c)) { continue }
        $ErrorActionPreference = 'Continue'
        $v = (& $c -version 2>&1 | Out-String)
        if ($v -match 'version "(\d+)' -and [int]$Matches[1] -ge 11) { return $c }
    }
    throw "Java 11+ not found. Install a JDK 17 (e.g. Microsoft OpenJDK) or set JAVA_HOME."
}

function Find-Adb {
    $bundled = Join-Path $PSScriptRoot '..\tools\platform-tools\adb.exe'
    if (Test-Path $bundled) { return (Resolve-Path $bundled).Path }
    $sdkAdb = "$(Find-AndroidSdk)\platform-tools\adb.exe"
    if (Test-Path $sdkAdb) { return $sdkAdb }
    return 'adb'
}

# Native tools write progress to stderr; Windows PowerShell 5 would turn that into terminating errors.
function Invoke-Tool([string]$exe, [string[]]$arguments) {
    $ErrorActionPreference = 'Continue'
    & $exe @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($exe)) failed with exit code $LASTEXITCODE" }
}

function Get-TargetDir([string]$package) {
    $dir = Join-Path $PSScriptRoot "targets\$package"
    New-Item -ItemType Directory -Force $dir | Out-Null
    return $dir
}
