param([string]$RunnerPath = (Join-Path $PSScriptRoot '..\check.sh'))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$bash = 'C:\Program Files\Git\bin\bash.exe'
if (-not (Test-Path -LiteralPath $bash)) { throw 'Git for Windows Bash is required.' }
$RunnerPath = (Resolve-Path -LiteralPath $RunnerPath).ProviderPath
# Старый runner игнорирует подмену Unity: его нельзя запускать даже для RED.
if (-not [IO.File]::ReadAllText($RunnerPath).Contains('PORTAL_UNITY')) {
    throw 'FAIL runner has no PORTAL_UNITY override; refusing to launch real Unity for RED.'
}
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $tempRoot ('portal-runner-tests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$utf8 = New-Object Text.UTF8Encoding($false)
$script:passed = 0
$script:failed = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function New-Checkout([string]$Name) {
    $path = Join-Path $testRoot $Name
    $null = New-Item -ItemType Directory -Path (Join-Path $path 'tools'), (Join-Path $path 'ProjectSettings'), (Join-Path $path 'Temp')
    # Даже регрессия PORTAL_UNITY не должна запустить установленный редактор.
    $runnerSource = [IO.File]::ReadAllText($RunnerPath)
    $forbiddenUnity = (Join-Path $testRoot 'REAL-UNITY-FORBIDDEN.exe').Replace('\', '/')
    $runnerSource = $runnerSource.Replace('C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe', $forbiddenUnity)
    $runnerSource = $runnerSource.Replace('/c/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe', $forbiddenUnity)
    [IO.File]::WriteAllText((Join-Path $path 'tools\check.sh'), $runnerSource, $utf8)
    Copy-Item -LiteralPath (Join-Path (Split-Path $RunnerPath) 'verify-check.ps1') -Destination (Join-Path $path 'tools\verify-check.ps1')
    [IO.File]::WriteAllText((Join-Path $path 'ProjectSettings\ProjectVersion.txt'), 'm_EditorVersion: 6000.5.9f1', $utf8)
    & git -C $path init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed.' }
    & git -C $path -c user.name=PortalTests -c user.email=portal-tests@example.invalid commit --allow-empty --quiet -m $Name
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git commit failed.' }
    return $path
}

function Invoke-Runner([string]$Cwd, [string]$Check = 'Seam', [string]$Argument = '', [string]$ProjectOverride = '') {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $bash
    $start.WorkingDirectory = $Cwd
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $scriptFile = (Join-Path $script:checkoutA 'tools\check.sh').Replace('\', '/')
    $start.Arguments = '--noprofile --norc "' + $scriptFile + '" ' + $Check
    if ($Argument) { $start.Arguments += ' "' + $Argument.Replace('"', '\"') + '"' }
    $start.EnvironmentVariables['PORTAL_UNITY'] = $script:fakeUnity
    $start.EnvironmentVariables['FAKE_TRACE'] = $script:trace
    $start.EnvironmentVariables['PORTAL_PROJECT'] = $ProjectOverride
    $start.EnvironmentVariables['PATH'] = 'C:\Program Files\Git\usr\bin;C:\Program Files\Git\mingw64\bin;C:\Windows\System32;C:\Windows\System32\WindowsPowerShell\v1.0'
    foreach ($key in $script:fakeOptions.Keys) { $start.EnvironmentVariables[$key] = $script:fakeOptions[$key] }
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    $null = $process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(20000)) {
        # Завершаем только дерево выделенного тестового процесса, никогда Unity пользователя.
        & "$env:SystemRoot\System32\taskkill.exe" /PID $process.Id /T /F | Out-Null
        throw 'Runner test exceeded the 20 second limit.'
    }
    $result = @{ Exit = $process.ExitCode; Output = $stdout.Result + $stderr.Result }
    $process.Dispose()
    return $result
}

function Test-Runner([string]$Name, [scriptblock]$Body) {
    $script:fakeOptions = @{}
    $script:trace = Join-Path $testRoot ([Guid]::NewGuid().ToString('N') + '.trace')
    try {
        & $Body
        $script:passed++
        Write-Output "PASS $Name"
    } catch {
        $script:failed++
        Write-Output "FAIL $Name : $($_.Exception.Message)"
    }
}

function Get-Calls { return @(Get-Content -LiteralPath $script:trace | ForEach-Object { ConvertFrom-Json $_ }) }

function Assert-EditorProjectArguments($Calls, [string]$ExpectedProject) {
    $editorCalls = @($Calls | Where-Object { $_.stage -in @('warmup', 'build') })
    Assert-True ($editorCalls.Count -eq 2) 'Expected both editor stages.'
    $expected = [IO.Path]::GetFullPath($ExpectedProject).TrimEnd('\')
    foreach ($call in $editorCalls) {
        Assert-True (-not [string]::IsNullOrEmpty($call.projectArg)) "Missing -projectPath at $($call.stage)."
        $actual = [IO.Path]::GetFullPath($call.projectArg).TrimEnd('\')
        Assert-True ([StringComparer]::OrdinalIgnoreCase.Equals($actual, $expected)) "Wrong -projectPath at $($call.stage)."
    }
}

try {
    $script:checkoutA = New-Checkout 'checkout A'
    $checkoutB = New-Checkout 'checkout B'
    $script:fakeUnity = Join-Path $testRoot 'FakeUnity.exe'
    # Подменяем внешние процессы; логика runner и verifier исполняется непосредственно.
    $fakeSource = @'
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public static class FakeUnity {
    static string Env(string name) { return Environment.GetEnvironmentVariable(name) ?? ""; }
    static int Code(string name) { int code; return Int32.TryParse(Env(name), out code) ? code : 0; }
    static string Arg(string[] args, string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
    public static int Main(string[] args) {
        var json = new JavaScriptSerializer();
        bool editor = Array.IndexOf(args, "-batchmode") >= 0;
        string method = Arg(args, "-executeMethod");
        string stage = editor ? (method == "" ? "warmup" : "build") : "player";
        string log = Arg(args, editor ? "-logFile" : "-logfile");
        string project = Env("PORTAL_CHECK_PROJECT");
        string check = Env("PORTAL_CHECK_NAME");
        string output = Env("PORTAL_CHECK_OUTPUT");
        var call = new Dictionary<string, object> {
            {"stage", stage}, {"project", project}, {"projectArg", Arg(args, "-projectPath")},
            {"cwd", Environment.CurrentDirectory}, {"commit", Env("PORTAL_CHECK_COMMIT")},
            {"runId", Env("PORTAL_CHECK_RUN_ID")}, {"output", output}, {"check", check},
            {"method", method}, {"width", Arg(args, "-screen-width")}, {"height", Arg(args, "-screen-height")},
            {"label", Env("PORTAL_LABEL")}, {"executable", Assembly.GetExecutingAssembly().Location}
        };
        File.AppendAllText(Env("FAKE_TRACE"), json.Serialize(call) + "\n");
        if (log != "") File.WriteAllText(log, "Fake " + stage + "\n");
        if (stage == "warmup") return Code("FAKE_WARMUP_EXIT");
        if (stage == "build") {
            if (Env("FAKE_NO_EXE") == "1") return Code("FAKE_BUILD_EXIT");
            string dir = "Build" + check + "Check", exe = check + "Check.exe";
            if (check == "AutoWire") { dir = "BuildAutoWire"; exe = "AutoWire.exe"; }
            if (check == "Performance") { dir = "BuildPortalPerformanceCheck"; exe = "PortalPerformanceCheck.exe"; }
            if (check == "Visibility") { dir = "BuildPortalVisibilityCheck"; exe = "PortalVisibilityCheck.exe"; }
            Directory.CreateDirectory(Path.Combine(project, dir));
            File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(project, dir, exe), true);
            return Code("FAKE_BUILD_EXIT");
        }
        var result = new Dictionary<string, object> {
            {"check", check}, {"completed", true}, {"status", "Passed"}, {"commit", Env("PORTAL_CHECK_COMMIT")},
            {"projectPath", project}, {"runId", Env("PORTAL_CHECK_RUN_ID")}, {"frameCount", 120},
            {"crossingCount", 0}, {"failureReason", ""}
        };
        if (Env("FAKE_REPORT") == "wrong-run") result["runId"] = "previous-run";
        if (Env("FAKE_REPORT") == "initial") File.AppendAllText(log, "[SeamCheck] starting\n");
        else if (Env("FAKE_REPORT") != "missing") File.AppendAllText(log, "[PortalCheckResult] " + json.Serialize(result) + "\n");
        return Code("FAKE_PLAYER_EXIT");
    }
}
'@
    Add-Type -TypeDefinition $fakeSource -ReferencedAssemblies System.Web.Extensions -OutputAssembly $script:fakeUnity -OutputType ConsoleApplication

    foreach ($cwd in @($script:checkoutA, (Join-Path $script:checkoutA 'tools'))) {
        Test-Runner "project root from script, cwd=$cwd" {
            $result = Invoke-Runner $cwd
            Assert-True ($result.Exit -eq 0) $result.Output
            $calls = Get-Calls
            Assert-True (($calls.stage -join ',') -eq 'warmup,build,player') 'Stage order mismatch.'
            Assert-EditorProjectArguments $calls $script:checkoutA
            $expectedCommit = (& git -C $script:checkoutA rev-parse HEAD).Trim()
            foreach ($call in $calls) {
                Assert-True ($call.project.Replace('/', '\') -eq $script:checkoutA) 'Wrong project identity.'
                Assert-True ($call.cwd -eq $script:checkoutA) 'Wrong process cwd.'
                Assert-True ($call.commit -ceq $expectedCommit) 'Wrong commit identity.'
                Assert-True ($call.runId -match '^[0-9a-f]{32}$') 'Missing unique run ID.'
                Assert-True ($call.output.Replace('/', '\').StartsWith((Join-Path $script:checkoutA 'Logs\checks\Seam-'))) 'Wrong log directory.'
            }
            Assert-True ($calls[0].runId -ceq $calls[2].runId) 'Identity changed between stages.'
            foreach ($file in @('warmup.log', 'build.log', 'player.log')) {
                Assert-True (Test-Path -LiteralPath (Join-Path $calls[0].output $file)) "Missing $file"
            }
            Assert-True ($calls[1].method -ceq 'SeamCheckBuilder.BuildPlayer') 'Wrong Seam builder.'
            Assert-True ($calls[2].width -eq '1280' -and $calls[2].height -eq '720') 'Wrong default resolution.'
        }
    }
    Test-Runner 'explicit second checkout overrides script checkout' {
        $result = Invoke-Runner $testRoot 'Seam' '' $checkoutB
        Assert-True ($result.Exit -eq 0) $result.Output
        $expectedCommit = (& git -C $checkoutB rev-parse HEAD).Trim()
        $calls = Get-Calls
        Assert-EditorProjectArguments $calls $checkoutB
        foreach ($call in $calls) {
            Assert-True ($call.project.Replace('/', '\') -eq $checkoutB) 'Override ignored.'
            Assert-True ($call.commit -ceq $expectedCommit) 'Wrong checkout commit.'
        }
    }
    Test-Runner 'fresh identity and separate output for every invocation' {
        $first = Invoke-Runner $testRoot
        $second = Invoke-Runner $testRoot
        Assert-True ($first.Exit -eq 0 -and $second.Exit -eq 0) ($first.Output + $second.Output)
        $calls = Get-Calls
        Assert-True ($calls[0].runId -cne $calls[3].runId -and $calls[0].output -cne $calls[3].output) 'Reused identity or logs.'
    }
    foreach ($stage in @('WARMUP', 'BUILD')) {
        Test-Runner "$stage failure short circuits" {
            $script:fakeOptions["FAKE_${stage}_EXIT"] = '17'
            $result = Invoke-Runner $testRoot
            Assert-True ($result.Exit -eq 17) $result.Output
            $expectedStages = if ($stage -eq 'WARMUP') { 'warmup' } else { 'warmup,build' }
            Assert-True (((Get-Calls).stage -join ',') -eq $expectedStages) 'Launched a later stage after failure.'
        }
    }
    foreach ($report in @('missing', 'initial', 'wrong-run')) {
        Test-Runner "report $report never passes" {
            $script:fakeOptions['FAKE_REPORT'] = $report
            $result = Invoke-Runner $testRoot
            Assert-True ($result.Exit -ne 0) 'Invalid or absent result accepted.'
            Assert-True (((Get-Calls).stage -join ',') -eq 'warmup,build,player') $result.Output
        }
    }
    Test-Runner 'player crash overrides valid final record' {
        $script:fakeOptions['FAKE_PLAYER_EXIT'] = '29'
        $result = Invoke-Runner $testRoot
        Assert-True ($result.Exit -ne 0) 'Player crash was accepted.'
    }
    foreach ($stage in @('WARMUP', 'BUILD', 'PLAYER')) {
        Test-Runner "$stage Windows exit 256 cannot wrap to success" {
            $script:fakeOptions["FAKE_${stage}_EXIT"] = '256'
            $result = Invoke-Runner $testRoot
            Assert-True ($result.Exit -ne 0) 'Nonzero native exit was truncated to success.'
            $expectedStages = if ($stage -eq 'WARMUP') { 'warmup' } elseif ($stage -eq 'BUILD') { 'warmup,build' } else { 'warmup,build,player' }
            Assert-True (((Get-Calls).stage -join ',') -eq $expectedStages) 'Failed native stage did not short circuit.'
        }
    }
    Test-Runner 'stale executable is not run and sibling files remain' {
        $script:fakeOptions['FAKE_NO_EXE'] = '1'
        $null = New-Item -ItemType Directory -Path (Join-Path $script:checkoutA 'BuildSeamCheck') -Force
        Copy-Item -LiteralPath $script:fakeUnity -Destination (Join-Path $script:checkoutA 'BuildSeamCheck\SeamCheck.exe') -Force
        $keep = Join-Path $script:checkoutA 'BuildSeamCheck\keep.txt'
        [IO.File]::WriteAllText($keep, 'preserve', $utf8)
        $result = Invoke-Runner $testRoot
        Assert-True ($result.Exit -ne 0) 'Stale executable was accepted.'
        Assert-True (((Get-Calls).stage -join ',') -eq 'warmup,build') 'Stale player was launched.'
        Assert-True ([IO.File]::ReadAllText($keep) -ceq 'preserve') 'Sibling file modified.'
    }
    foreach ($check in @('Color', 'Ghost', 'Rotate', 'Cross', 'Look', 'Cinemachine', 'Bubble', 'Close', 'Light', 'Prefab', 'AutoWire', 'Setup', 'SandboxParity', 'Performance', 'Visibility')) {
        Test-Runner "$check builder and player mapping" {
            $result = Invoke-Runner $testRoot $check
            Assert-True ($result.Exit -eq 0) $result.Output
            $calls = Get-Calls
            $method = $check + 'CheckBuilder.BuildPlayer'
            if ($check -eq 'AutoWire') { $method = 'AutoWireCheckBuilder.BuildPlayer' }
            if ($check -eq 'Setup') { $method = 'SetupCheckBuild.BuildPlayer' }
            if ($check -eq 'Performance') { $method = 'PortalPerformanceCheckBuilder.BuildPlayer' }
            if ($check -eq 'Visibility') { $method = 'PortalVisibilityCheckBuilder.BuildPlayer' }
            Assert-True ($calls[1].method -ceq $method) 'Wrong builder mapping.'
            $width = if ($check -eq 'Performance') { '1920' } else { '1280' }
            $height = if ($check -eq 'Performance') { '1080' } else { '720' }
            Assert-True ($calls[2].width -eq $width -and $calls[2].height -eq $height) 'Wrong resolution.'
        }
    }
    foreach ($argument in @('INVALID', 'BAD-NAME=1', 'PORTAL_CHECK_RUN_ID=forged', 'portal_check_name=Forged', 'PATH=wrong', 'BASH_ENV=wrong', 'PORTAL_PROJECT=wrong')) {
        Test-Runner "unsafe or reserved override rejected: $argument" {
            $result = Invoke-Runner $testRoot 'Seam' $argument
            Assert-True ($result.Exit -ne 0) 'Invalid override accepted.'
            Assert-True (-not (Test-Path -LiteralPath $script:trace)) 'Unity launched before input validation.'
        }
    }
    Test-Runner 'shell metacharacters remain literal values' {
        $result = Invoke-Runner $testRoot 'Seam' 'PORTAL_LABEL=$(false); literal & value'
        Assert-True ($result.Exit -eq 0) $result.Output
        Assert-True ((Get-Calls)[0].label -ceq '$(false); literal & value') 'Environment value was evaluated or split.'
    }
    Test-Runner 'environment overrides cannot mutate runner variables' {
        $result = Invoke-Runner $testRoot 'Seam' 'PROJECT_POSIX=wrong-directory'
        Assert-True ($result.Exit -eq 0) $result.Output
        Assert-True ((Get-Calls)[2].project.Replace('/', '\') -eq $script:checkoutA) 'Internal project variable changed.'
    }
    Test-Runner 'missing PowerShell fails before any launch' {
        $script:fakeOptions['PATH'] = 'C:\Program Files\Git\usr\bin;C:\Program Files\Git\mingw64\bin;C:\Windows\System32'
        $result = Invoke-Runner $testRoot
        Assert-True ($result.Exit -ne 0) 'Missing verifier runtime was accepted.'
        Assert-True (-not (Test-Path -LiteralPath $script:trace)) 'Unity launched without a verifier runtime.'
    }
    $pwshCommand = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($pwshCommand) {
        Test-Runner 'pwsh fallback without Windows PowerShell in PATH' {
            $script:fakeOptions['PATH'] = 'C:\Program Files\Git\usr\bin;C:\Program Files\Git\mingw64\bin;C:\Windows\System32;' + (Split-Path $pwshCommand.Source)
            $result = Invoke-Runner $testRoot
            Assert-True ($result.Exit -eq 0) $result.Output
        }
    }
    Test-Runner 'UnityLockfile blocks before launch' {
        $lock = Join-Path $script:checkoutA 'Temp\UnityLockfile'
        $null = New-Item -ItemType File -Path $lock
        try {
            $result = Invoke-Runner $testRoot
            Assert-True ($result.Exit -ne 0) 'Unity lock ignored.'
            Assert-True (-not (Test-Path -LiteralPath $script:trace)) 'Locked Unity project was launched.'
        } finally { Remove-Item -LiteralPath $lock }
    }
    Test-Runner 'existing runner lock is not guessed stale' {
        $lock = Join-Path $script:checkoutA 'Logs\checks\.runner-lock'
        $null = New-Item -ItemType Directory -Path $lock
        try {
            $result = Invoke-Runner $testRoot
            Assert-True ($result.Exit -ne 0) 'Runner lock ignored.'
            Assert-True (-not (Test-Path -LiteralPath $script:trace)) 'Concurrent runner launched Unity.'
            Assert-True (Test-Path -LiteralPath $lock) 'Foreign lock removed.'
        } finally { Remove-Item -LiteralPath $lock }
    }
    Test-Runner 'invalid project override fails before launch' {
        $result = Invoke-Runner $testRoot 'Seam' '' $testRoot
        Assert-True ($result.Exit -ne 0) 'Non-Unity project accepted.'
        Assert-True (-not (Test-Path -LiteralPath $script:trace)) 'Invalid project launched Unity.'
    }
    Write-Output "Runner tests: $script:passed passed, $script:failed failed (20 seconds maximum per invocation; fake processes only)."
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne $tempRoot.TrimEnd('\') -or
        [IO.Path]::GetFileName($resolved) -notmatch '^portal-runner-tests-[0-9a-f]{32}$') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if ($script:failed -gt 0) { exit 1 }
exit 0
