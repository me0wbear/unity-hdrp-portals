param([string]$VerifierPath = (Join-Path $PSScriptRoot '..\verify-check.ps1'))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$powerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempRoot ('portal-verifier-tests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$logPath = Join-Path $testRoot 'player.log'
$utf8 = New-Object Text.UTF8Encoding($false)
$script:passed = 0
$script:failed = 0
$commit = '0123456789abcdef0123456789abcdef01234567'
$project = 'C:\Portal Tests\Project'
$runId = '0123456789abcdef0123456789abcdef'

function New-Result {
    return [ordered]@{
        check = 'Seam'; completed = $true; status = 'Passed'; commit = $commit
        projectPath = $project; runId = $runId; frameCount = 120
        crossingCount = 0; failureReason = ''
    }
}

function ConvertTo-Record($Result) {
    return '[PortalCheckResult] ' + (ConvertTo-Json -InputObject $Result -Compress -Depth 10)
}

function Test-Log([string]$Name, [AllowNull()]$Content, [int]$ExpectedExit, [int]$PlayerExit = 0) {
    $caseLog = $logPath
    if ($null -eq $Content) {
        $caseLog = Join-Path $testRoot 'missing.log'
    } else {
        [IO.File]::WriteAllText($caseLog, $Content, $utf8)
    }
    $output = & $powerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass `
        -File $VerifierPath -LogPath $caseLog -ExpectedCheck 'Seam' `
        -ExpectedCommit $commit -ExpectedProjectPath $project -ExpectedRunId $runId `
        -PlayerExitCode $PlayerExit 2>&1
    $actualExit = $LASTEXITCODE
    if ($actualExit -eq $ExpectedExit) {
        $script:passed++
        Write-Output "PASS $Name"
    } else {
        $script:failed++
        Write-Output "FAIL $Name expected=$ExpectedExit actual=$actualExit : $output"
    }
}

try {
    if (-not (Test-Path -LiteralPath $VerifierPath -PathType Leaf)) {
        throw 'FAIL verifier implementation is missing; valid completed result cannot pass.'
    }
    $VerifierPath = (Resolve-Path -LiteralPath $VerifierPath).ProviderPath
    $valid = ConvertTo-Record (New-Result)
    Test-Log 'valid completed result' $valid 0
    Test-Log 'valid result surrounded by Unity diagnostics' "[SeamCheck] starting`r`n$valid`r`nUnityEngine.Debug:Log (object)`r`n" 0
    Test-Log 'empty log' '' 1
    Test-Log 'missing log' $null 1
    Test-Log 'initial Seam tag only' '[SeamCheck] starting' 1
    Test-Log 'player failure overrides Passed' $valid 1 23
    Test-Log 'negative player exit' $valid 1 -1
    Test-Log 'malformed JSON' '[PortalCheckResult] {broken' 1
    Test-Log 'no JSON payload' '[PortalCheckResult]' 1
    Test-Log 'duplicate records' "$valid`n$valid" 1
    $result = New-Result; $result.status = 'Failed'
    Test-Log 'conflicting records' ($valid + "`n" + (ConvertTo-Record $result)) 1
    Test-Log 'malformed record after valid record' "$valid`n[PortalCheckResult] {broken" 1
    Test-Log 'JSON array instead of object' ('[PortalCheckResult] [' + $valid.Substring(20) + ']') 1
    Test-Log 'JSON null' '[PortalCheckResult] null' 1
    Test-Log 'trailing JSON data' ($valid + ' {}') 1
    Test-Log 'multiline result' ($valid.Replace('{', "{`n")) 1
    Test-Log 'trailing comma' ($valid.Substring(0, $valid.Length - 1) + ',}') 1
    Test-Log 'JSON comment' ($valid.Replace('{', '{/* comment */')) 1
    Test-Log 'duplicate property' ($valid.Replace('{', '{"status":"Failed",')) 1
    Test-Log 'duplicate escaped property' ($valid.Replace('{', '{"st\u0061tus":"Failed",')) 1
    Test-Log 'leading-zero number' ($valid.Replace('"frameCount":120', '"frameCount":0120')) 1
    Test-Log 'wrong tag case' ($valid.Replace('PortalCheckResult', 'portalcheckresult')) 1
    foreach ($field in @('check', 'completed', 'status', 'commit', 'projectPath', 'runId', 'frameCount', 'crossingCount', 'failureReason')) {
        $result = New-Result; $result.Remove($field)
        Test-Log "missing $field" (ConvertTo-Record $result) 1
        $result = New-Result; $result[$field] = $null
        Test-Log "null $field" (ConvertTo-Record $result) 1
    }
    $invalidValues = @{
        check = @('seam', 'Ghost', 7, @('Seam'))
        completed = @($false, 'true', 1)
        status = @('Failed', 'Blocked', 'passed', 1)
        commit = @('0123456', 'fedcba9876543210fedcba9876543210fedcba98', $commit.ToUpperInvariant(), 1)
        projectPath = @('C:\Other\Project', 'Project', '\Portal Tests\Project', 'C:Portal Tests\Project', 'C:\Portal Tests\Other\..\Project', 1)
        runId = @('other-run', $runId.ToUpperInvariant(), 1)
        frameCount = @(0, -1, 1.5, '120', $true, @(120))
        crossingCount = @(-1, 0.5, '0', $false)
        failureReason = @('unexpected failure', 7, $false)
    }
    foreach ($field in $invalidValues.Keys) {
        foreach ($value in $invalidValues[$field]) {
            $result = New-Result; $result[$field] = $value
            Test-Log ("invalid {0}={1}" -f $field, (ConvertTo-Json -InputObject $value -Compress)) (ConvertTo-Record $result) 1
        }
    }
    $result = New-Result; $result.projectPath = 'c:/PORTAL TESTS/project/'
    Test-Log 'Windows path case separators and trailing separator' (ConvertTo-Record $result) 0
    $result = New-Result; $result.crossingCount = 3
    Test-Log 'positive crossing count' (ConvertTo-Record $result) 0
    $result = New-Result; $result['metrics'] = @{ label = 'escaped "text"'; values = @(1, $false, $null) }
    Test-Log 'additional metrics do not invalidate contract' (ConvertTo-Record $result) 0
    $result = New-Result; $result.Remove('check'); $result['Check'] = 'Seam'
    Test-Log 'mandatory field names are case sensitive' (ConvertTo-Record $result) 1
    Write-Output "Verifier tests: $script:passed passed, $script:failed failed (Windows PowerShell $($PSVersionTable.PSVersion))."
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne $tempRoot.TrimEnd('\') -or
        [IO.Path]::GetFileName($resolved) -notmatch '^portal-verifier-tests-[0-9a-f]{32}$') {
        throw 'Unsafe test cleanup path.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if ($script:failed -gt 0) { exit 1 }
exit 0
