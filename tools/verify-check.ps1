param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$ExpectedCheck,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [Parameter(Mandatory = $true)][string]$ExpectedProjectPath,
    [Parameter(Mandatory = $true)][string]$ExpectedRunId,
    [Parameter(Mandatory = $true)][int]$PlayerExitCode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ConvertFrom-Json допускает некоторые расширения JSON. Сначала проверяем
# строгую грамматику и уникальность ключей, затем используем штатные типы .NET.
function Assert-JsonValue([int]$Depth = 0) {
    if ($Depth -gt 64 -or $script:position -ge $script:tokens.Count) { throw 'Invalid JSON value.' }
    $token = $script:tokens[$script:position++]
    if ($token -eq '{' -or $token -eq '[') {
        $isObject = $token -eq '{'
        $end = if ($isObject) { '}' } else { ']' }
        $keys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        if ($script:position -lt $script:tokens.Count -and $script:tokens[$script:position] -eq $end) {
            $script:position++; return
        }
        while ($script:position -lt $script:tokens.Count) {
            if ($isObject) {
                $key = $script:tokens[$script:position++]
                if (-not $key.StartsWith('"')) { throw 'Invalid JSON property.' }
                $decoded = (ConvertFrom-Json ('{"key":' + $key + '}')).key
                if (-not $keys.Add($decoded)) { throw 'Duplicate JSON property.' }
                if ($script:position -ge $script:tokens.Count -or $script:tokens[$script:position++] -ne ':') {
                    throw 'Missing JSON colon.'
                }
            }
            Assert-JsonValue ($Depth + 1)
            if ($script:position -ge $script:tokens.Count) { throw 'Unterminated JSON container.' }
            $separator = $script:tokens[$script:position++]
            if ($separator -eq $end) { return }
            if ($separator -ne ',') { throw 'Invalid JSON separator.' }
        }
        throw 'Unterminated JSON container.'
    }
    if ($token -in @('}', ']', ',', ':')) { throw 'Invalid JSON value.' }
}

function ConvertFrom-StrictJson([string]$Json) {
    $pattern = '\G[ \t\r\n]*("(?:[^"\\\x00-\x1f]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*"|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?|true|false|null|[{}\[\],:])'
    $lexer = [regex]::new($pattern, [Text.RegularExpressions.RegexOptions]::None, [TimeSpan]::FromSeconds(2))
    $script:tokens = New-Object 'System.Collections.Generic.List[string]'
    $offset = 0
    while ($offset -lt $Json.Length) {
        $match = $lexer.Match($Json, $offset)
        if (-not $match.Success) {
            if ($Json.Substring($offset) -cmatch '^[ \t\r\n]*$') { break }
            throw 'Invalid JSON token.'
        }
        $script:tokens.Add($match.Groups[1].Value)
        $offset += $match.Length
    }
    $script:position = 0
    Assert-JsonValue
    if ($script:position -ne $script:tokens.Count -or $script:tokens[0] -ne '{') { throw 'Expected one JSON object.' }
    return ConvertFrom-Json -InputObject $Json
}

function Get-CanonicalWindowsPath([string]$Path) {
    $normalized = $Path.Replace('/', '\')
    if ($normalized -cnotmatch '^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+)') { throw 'Project path must be absolute.' }
    $canonical = [IO.Path]::GetFullPath($normalized).TrimEnd('\')
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($normalized.TrimEnd('\'), $canonical)) {
        throw 'Project path must be canonical.'
    }
    return $canonical
}

try {
    if ($PlayerExitCode -ne 0) { throw "Player exited with code $PlayerExitCode." }
    if ($ExpectedCommit -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') { throw 'Expected commit must be a full Git SHA.' }
    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) { throw "Log file not found: $LogPath" }
    $records = @([IO.File]::ReadLines([IO.Path]::GetFullPath($LogPath)) | Where-Object { $_.Contains('[PortalCheckResult]') })
    if ($records.Count -ne 1) { throw "Expected exactly one final record, found $($records.Count)." }
    if ($records[0] -cnotmatch '^\[PortalCheckResult\] (.+)$') { throw 'Invalid final record format.' }
    $result = ConvertFrom-StrictJson $Matches[1]
    $required = @('check', 'completed', 'status', 'commit', 'projectPath', 'runId', 'frameCount', 'crossingCount', 'failureReason')
    foreach ($field in $required) {
        if ($result.PSObject.Properties.Name -cnotcontains $field) { throw "Missing field: $field" }
    }
    foreach ($field in @('check', 'status', 'commit', 'projectPath', 'runId', 'failureReason')) {
        if ($result.$field -isnot [string]) { throw "Field must be a string: $field" }
    }
    if ($result.completed -isnot [bool] -or -not $result.completed) { throw 'Check is not completed.' }
    if (-not [StringComparer]::Ordinal.Equals($result.status, 'Passed')) { throw "Check status: $($result.status). Reason: $($result.failureReason)" }
    if ($result.failureReason.Length -ne 0) { throw 'Passed result must have an empty failureReason.' }
    foreach ($field in @('frameCount', 'crossingCount')) {
        if ($result.$field -isnot [int] -and $result.$field -isnot [long]) { throw "Field must be an integer: $field" }
    }
    if ($result.frameCount -le 0 -or $result.crossingCount -lt 0) { throw 'Invalid frame or crossing count.' }
    if (-not [StringComparer]::Ordinal.Equals($result.check, $ExpectedCheck)) { throw 'Check name mismatch.' }
    if (-not [StringComparer]::Ordinal.Equals($result.commit, $ExpectedCommit)) { throw 'Commit mismatch.' }
    if (-not [StringComparer]::Ordinal.Equals($result.runId, $ExpectedRunId)) { throw 'Run ID mismatch.' }
    $actualProject = Get-CanonicalWindowsPath $result.projectPath
    $expectedProject = Get-CanonicalWindowsPath $ExpectedProjectPath
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualProject, $expectedProject)) { throw 'Project path mismatch.' }
    Write-Output "Passed: $ExpectedCheck; runId=$ExpectedRunId; frames=$($result.frameCount); crossings=$($result.crossingCount)"
    exit 0
} catch {
    Write-Output "Portal check verification failed: $($_.Exception.Message) Log: $LogPath"
    exit 1
}
