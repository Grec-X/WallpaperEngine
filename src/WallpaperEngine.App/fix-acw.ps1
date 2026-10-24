param([string]$AcwDir)
# Rewrite the illegally-generated top-level WallpaperService.Engine bridge:
#  1. move the class declaration inside the Service ACW (Java requires inner-class
#     subclasses to be declared within an enclosing class)
#  2. super(p0) -> super()  (Engine's Java ctor takes no args)
#  3. convert the static-field + static-initializer block into a ctor-time
#     Runtime.register call so the source is Java 8 compatible (inner classes
#     cannot have static members before Java 16)
# See stage-4 log for the upstream generator bug this works around.
$ErrorActionPreference = 'Stop'

$serviceFile = Join-Path $AcwDir 'VideoWallpaperService.java'
$engineFile = Join-Path $AcwDir 'VideoWallpaperService$VideoWallpaperService_VideoEngine.java'
$defaultEngineFile = Join-Path $AcwDir 'VideoWallpaperService_VideoEngine.java'

if (-not (Test-Path $engineFile)) {
    Write-Output 'fix-acw: engine ACW not found, skip'
    exit 0
}

# idempotency for incremental builds
$svcExisting = [IO.File]::ReadAllText($serviceFile)
if ($svcExisting.Contains('VideoWallpaperService_VideoEngine')) {
    Write-Output 'fix-acw: already fixed, skip'
    Remove-Item $engineFile -Force -ErrorAction SilentlyContinue
    Remove-Item $defaultEngineFile -Force -ErrorAction SilentlyContinue
    exit 0
}

$lines = Get-Content $engineFile -Encoding UTF8 | Where-Object { $_ -notmatch '^package\s' }
$body = $lines -join "`r`n"
$body = $body.Replace('VideoWallpaperService$VideoWallpaperService_VideoEngine', 'VideoWallpaperService_VideoEngine')
$body = $body.Replace('super (p0);', 'super ();')
# match the Debug-generated (runtime-verified) inner-class ctor shape:
# parameterless ctor + outer instance passed to the managed activator
$body = $body.Replace('public VideoWallpaperService_VideoEngine (android.service.wallpaper.WallpaperService p0)', 'public VideoWallpaperService_VideoEngine ()')
$body = $body.Replace('new java.lang.Object[] { p0 }', 'new java.lang.Object[] { VideoWallpaperService.this }')

# extract managed type name from the register call inside the static block
if ($body -notmatch '(?s)mono\.android\.Runtime\.register\s*\("(?<type>[^"]+)"') { throw 'fix-acw: register call not found' }
$typeName = $Matches['type']

# extract the __md_methods string expression: scan with a quote state machine so
# that semicolons INSIDE the string literals (jni signatures) do not end the scan
$sidx = $body.IndexOf('__md_methods')
$sidx = $body.IndexOf('=', $sidx) + 1
$eidx = -1
$inStr = $false
for ($i = $sidx; $i -lt $body.Length; $i++) {
    $ch = $body[$i]
    if ($ch -eq '"') { $inStr = -not $inStr }
    elseif ($ch -eq ';' -and -not $inStr) { $eidx = $i; break }
}
if ($eidx -lt 0) { throw 'fix-acw: __md_methods expression end not found' }
$expr = $body.Substring($sidx, $eidx - $sidx).Trim()

# remove the static field declaration and the whole static initializer block
$body = [regex]::Replace($body, '(?m)^\s*public static final String __md_methods;\s*\r?\n', '')
$body = [regex]::Replace($body, '(?s)\tstatic \{.*?\}\r?\n\r?\n', '')

# register at construction time (first construction binds native methods; ctor runs before any callback)
$ctorAnchor = 'if (getClass () == VideoWallpaperService_VideoEngine.class) {'
$registerCall = "`t`t`tmono.android.Runtime.register (`"$typeName`", VideoWallpaperService_VideoEngine.class, $expr);"
if ($body.Contains($ctorAnchor)) {
    $body = $body.Replace($ctorAnchor, $ctorAnchor + "`r`n" + $registerCall)
} else {
    throw 'fix-acw: ctor anchor not found'
}

$indented = ($body -split "`r`n" | ForEach-Object { if ($_ -eq '') { $_ } else { '    ' + $_ } }) -join "`r`n"

$svc = [IO.File]::ReadAllText($serviceFile)
$idx = $svc.LastIndexOf('}')
if ($idx -lt 0) { throw 'fix-acw: service ACW malformed' }
$svc = $svc.Insert($idx, "`r`n" + $indented + "`r`n")
[IO.File]::WriteAllText($serviceFile, $svc)

Remove-Item $engineFile -Force
if (Test-Path $defaultEngineFile) { Remove-Item $defaultEngineFile -Force }

Write-Output 'fix-acw: engine ACW rewritten as Java8-compatible inner class'
