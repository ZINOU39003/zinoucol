<#
.SYNOPSIS
  Builds a signed RRO overlay APK from targets\<package>\strings.xml and optionally installs and enables it.
.DESCRIPTION
  Translations go into values-<Locale> (default: ar), so they show once the system language is Arabic
  (MoreLocale2 or setprop). Use -AllLocales to put them in values/ instead (only wins where the
  target has no more specific translation for the current language).
  Third-party (non-static) overlays like this one can be enabled with `cmd overlay` on Android 8/9 (IHU624G).
.EXAMPLE
  .\build-overlay.ps1 -Package com.geely.settings -Install
#>
param(
    [Parameter(Mandatory)][string]$Package,
    [string]$Locale = 'ar',
    [switch]$AllLocales,
    [int]$Priority = 100,
    [int]$VersionCode = 0,
    [string]$Serial,
    [switch]$Install
)
. "$PSScriptRoot\common.ps1"

$bt = Find-BuildTools
$aapt2 = "$bt\aapt2.exe"
$zipalign = "$bt\zipalign.exe"
$apksignerJar = "$bt\lib\apksigner.jar"
$androidJar = Find-AndroidJar
$java = Find-Java

$dir = Get-TargetDir $Package
$source = Join-Path $dir 'strings.xml'
if (-not (Test-Path $source)) { throw "$source not found. Run extract-strings.ps1 first." }

$overlayPackage = "ar.overlay.$Package"
if ($VersionCode -le 0) { $VersionCode = [int]((Get-Date) - [datetime]'2024-01-01').TotalMinutes }

$work = Join-Path $dir 'build'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
$valuesDir = if ($Locale -and -not $AllLocales) { "values-$Locale" } else { 'values' }
New-Item -ItemType Directory -Force "$work\res\$valuesDir" | Out-Null

# Keep only translated strings and apply Android string escaping.
[xml]$doc = Get-Content $source -Raw -Encoding UTF8
$outDoc = [xml]'<?xml version="1.0" encoding="utf-8"?><resources/>'
$kept = 0
foreach ($node in $doc.resources.string) {
    if ($node.HasChildNodes -and $node.ChildNodes.Count -eq 1 -and $node.FirstChild.NodeType -eq 'Text') {
        $text = $node.InnerText.Trim()
        if (-not $text) { continue }
        $text = $text -replace "(?<!\\)'", "\'" -replace '(?<!\\)"', '\"'
        if ($text -match '^[@?]') { $text = "\$text" }
        $n = $outDoc.CreateElement('string')
        $n.SetAttribute('name', $node.name)
        $n.InnerText = $text
    } elseif ($node.HasChildNodes) {
        $n = $outDoc.ImportNode($node, $true)
    } else { continue }
    [void]$outDoc.DocumentElement.AppendChild($n)
    $kept++
}
if ($kept -eq 0) { throw "No translated strings in $source." }
$outDoc.Save("$work\res\$valuesDir\strings.xml")
Write-Host "$kept translated strings -> $valuesDir"

@"
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="$overlayPackage"
    android:versionCode="$VersionCode"
    android:versionName="1.0">
    <overlay android:targetPackage="$Package" android:priority="$Priority" android:isStatic="false" />
    <application android:hasCode="false" android:label="Arabic overlay: $Package" />
</manifest>
"@ | Set-Content "$work\AndroidManifest.xml" -Encoding UTF8

Invoke-Tool $aapt2 @('compile', '--dir', "$work\res", '-o', "$work\res.zip")
Invoke-Tool $aapt2 @('link', '-o', "$work\unsigned.apk", '-I', $androidJar,
    '--manifest', "$work\AndroidManifest.xml", '--no-resource-removal', '--min-sdk-version', '26', '--target-sdk-version', '28',
    "$work\res.zip")
Invoke-Tool $zipalign @('-f', '-p', '4', "$work\unsigned.apk", "$work\aligned.apk")

$keystore = Join-Path $PSScriptRoot 'overlay.keystore'
if (-not (Test-Path $keystore)) {
    $keytool = Join-Path (Split-Path $java) 'keytool.exe'
    Invoke-Tool $keytool @('-genkeypair', '-keystore', $keystore, '-storepass', 'android', '-keypass', 'android',
        '-alias', 'overlay', '-keyalg', 'RSA', '-keysize', '2048', '-validity', '10000',
        '-dname', 'CN=Geely Arabic Overlay')
}

$apk = Join-Path $PSScriptRoot "out\$overlayPackage.apk"
New-Item -ItemType Directory -Force (Split-Path $apk) | Out-Null
Invoke-Tool $java @('-jar', $apksignerJar, 'sign', '--ks', $keystore, '--ks-pass', 'pass:android',
    '--ks-key-alias', 'overlay', '--min-sdk-version', '26', '--out', $apk, "$work\aligned.apk")
Write-Host "Built $apk"

if ($Install) {
    $adb = Find-Adb
    $adbArgs = @(); if ($Serial) { $adbArgs = @('-s', $Serial) }
    Invoke-Tool $adb ($adbArgs + @('install', '-r', $apk))
    Start-Sleep -Seconds 2
    Invoke-Tool $adb ($adbArgs + @('shell', "cmd overlay enable --user 0 $overlayPackage"))
    & $adb @adbArgs shell "cmd overlay list $Package"
    Write-Host "Enabled $overlayPackage. Restart the target app (or reboot) to see the change."
}
