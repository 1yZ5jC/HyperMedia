param()
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$patched = 0
Get-ChildItem -LiteralPath $root -Directory -Recurse -Include "bin", "obj" -ErrorAction SilentlyContinue | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Recurse -Filter "AppxManifest.xml" -ErrorAction SilentlyContinue | ForEach-Object {
        $p = $_.FullName
        $raw = [System.IO.File]::ReadAllText($p)
        if ($raw -match "VCLibs") {
            $new = [System.Text.RegularExpressions.Regex]::Replace($raw, "(?s)<Dependencies>.*?</Dependencies>", "")
            [System.IO.File]::WriteAllText($p, $new)
            Write-Output "patched: $p"
            $patched++
        }
    }
}
if ($patched -eq 0) { Write-Output "nothing to patch" }