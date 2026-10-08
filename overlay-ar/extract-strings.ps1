<#
.SYNOPSIS
  Pulls an app from the head unit (or takes a local APK) and writes a translation file
  targets\<package>\strings.xml with every string resource left empty for translation.
.EXAMPLE
  .\extract-strings.ps1 -Package com.geely.settings
  .\extract-strings.ps1 -Apk C:\dump\Settings.apk
#>
param(
    [string]$Package,
    [string]$Apk,
    [string]$Serial,
    [switch]$Force
)
. "$PSScriptRoot\common.ps1"

$aapt2 = "$(Find-BuildTools)\aapt2.exe"

if (-not $Apk) {
    if (-not $Package) { throw "Pass -Package <name> (pull from unit) or -Apk <file>." }
    $adb = Find-Adb
    $adbArgs = @(); if ($Serial) { $adbArgs = @('-s', $Serial) }
    $paths = & $adb @adbArgs shell pm path $Package
    $remote = ($paths | Where-Object { $_ -match '^package:' } | ForEach-Object { $_ -replace '^package:', '' } |
        Sort-Object { if ($_ -match 'base\.apk$') { 0 } else { 1 } } | Select-Object -First 1)
    if (-not $remote) { throw "Package $Package not found on the unit." }
    $Apk = Join-Path (Get-TargetDir $Package) 'original.apk'
    Write-Host "Pulling $remote"
    Invoke-Tool $adb ($adbArgs + @('pull', $remote.Trim(), $Apk))
}

if (-not $Package) { $Package = (& $aapt2 dump packagename $Apk).Trim() }
$dir = Get-TargetDir $Package
$out = Join-Path $dir 'strings.xml'
if ((Test-Path $out) -and -not $Force) {
    throw "$out already exists (it may contain your translations). Use -Force to overwrite."
}

Write-Host "Reading resources of $Package"
$dump = & $aapt2 dump resources $Apk
if ($LASTEXITCODE -ne 0) { throw "aapt2 dump resources failed" }

$strings = [ordered]@{}
$current = $null
foreach ($line in $dump) {
    if ($line -match '^\s+resource 0x[0-9a-f]+ string/(\S+)') {
        $current = $Matches[1]
        $strings[$current] = @{}
        continue
    }
    if ($line -match '^\s+resource ' -or $line -match '^\s+type ') { $current = $null; continue }
    if ($current -and $line -match '^\s+\(([^)]*)\)\s+(?:\(styled string\)\s+)?"(.*)"') {
        $strings[$current][$Matches[1]] = $Matches[2]
    }
}

function Pick($values, [string[]]$configs) {
    foreach ($c in $configs) { if ($values.ContainsKey($c) -and $values[$c]) { return $values[$c] } }
    return $null
}

function Escape-Comment([string]$s) { ($s -replace '--', '- -') }

$sb = [Text.StringBuilder]::new()
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine("<!-- Arabic translation for $Package. Fill the empty strings; empty ones are skipped at build time. -->")
[void]$sb.AppendLine('<resources>')
$count = 0
foreach ($name in $strings.Keys) {
    $v = $strings[$name]
    $en = Pick $v @('en', 'en-rUS', 'en-rGB', '')
    $zh = Pick $v @('zh-rCN', 'zh', 'zh-rTW')
    if (-not $en -and -not $zh) { continue }
    $hint = @()
    if ($en) { $hint += "en: $en" }
    if ($zh -and $zh -ne $en) { $hint += "zh: $zh" }
    [void]$sb.AppendLine("    <!-- $(Escape-Comment ($hint -join ' | ')) -->")
    [void]$sb.AppendLine("    <string name=`"$name`"></string>")
    $count++
}
[void]$sb.AppendLine('</resources>')
[IO.File]::WriteAllText($out, $sb.ToString(), [Text.UTF8Encoding]::new($false))

Write-Host "Wrote $count strings to $out"
