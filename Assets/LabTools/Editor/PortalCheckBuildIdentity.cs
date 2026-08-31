using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Portals.Lab.Validation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public sealed class PortalCheckBuildIdentity : BuildPlayerProcessor, IPreprocessBuildWithReport, IProcessSceneWithReport
{
    private const string DependencyPath = "Logs/portal-check-build-state.json";
    private static PortalCheckIdentity preparedIdentity;
    private static string firstScenePath;
    public override int callbackOrder => -10000;

    public override void PrepareForBuild(BuildPlayerContext context)
    {
        firstScenePath = context.BuildPlayerOptions.scenes?.FirstOrDefault();
    }

    public static BuildPlayerOptions PrepareOptions(BuildPlayerOptions options, string check)
    {
        if (IsMigratedCheck(check)) options.options |= BuildOptions.CleanBuildCache;
        return options;
    }

    public static bool IsMigratedCheck(string check) => check == "Color" || check == "Seam"
        || check == "SandboxParity" || check == "Performance" || check == "Visibility";

    public static void RequireCleanBuild(BuildOptions options, string check)
    {
        if (IsMigratedCheck(check) && (options & BuildOptions.CleanBuildCache) == 0)
            throw new BuildFailedException("Certified portal checks require BuildOptions.CleanBuildCache.");
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        preparedIdentity = null;
        string check = Environment.GetEnvironmentVariable("PORTAL_CHECK_NAME");
        RequireCleanBuild(report.summary.options, check);
        if (IsMigratedCheck(check))
        {
            preparedIdentity = ReadIdentity(check, Environment.GetEnvironmentVariable("PORTAL_CHECK_COMMIT"),
                Environment.GetEnvironmentVariable("PORTAL_CHECK_PROJECT"),
                Environment.GetEnvironmentVariable("PORTAL_CHECK_RUN_ID"),
                Environment.GetEnvironmentVariable("PORTAL_CHECK_OUTPUT"));
            Directory.CreateDirectory(preparedIdentity.outputDirectory);
            File.WriteAllText(Path.Combine(preparedIdentity.outputDirectory, "build-identity.json"),
                JsonUtility.ToJson(preparedIdentity, true));
        }
        // Стабильная зависимость также инвалидирует проверочную сцену при следующей обычной сборке.
        // Изменяем только служебный файл Logs; исходные сцены и Resources не затрагиваются.
        string state = preparedIdentity == null ? "ordinary-build" : JsonUtility.ToJson(preparedIdentity);
        string dependencyFile = Path.Combine(Path.GetDirectoryName(Application.dataPath), DependencyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dependencyFile));
        if (!File.Exists(dependencyFile) || File.ReadAllText(dependencyFile) != state)
            File.WriteAllText(dependencyFile, state);
    }

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (!BuildPipeline.isBuildingPlayer || report == null) return;
        BuildPipelineContext.DependOnPath(DependencyPath);
        // buildIndex может быть -1 для сцены, переданной только через BuildPlayerOptions.
        if (preparedIdentity == null || scene.path != firstScenePath) return;
        Inject(scene, preparedIdentity);
    }

    public static void Inject(Scene scene, PortalCheckIdentity identity)
    {
        if (identity == null) return;
        string invalid = identity.Validate();
        if (invalid.Length != 0) throw new BuildFailedException(invalid);
        PortalCheckRun[] existing = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PortalCheckRun>(true)).ToArray();
        if (existing.Length > 1) throw new BuildFailedException("Multiple portal run contexts in build scene.");
        PortalCheckRun context;
        if (existing.Length == 1) context = existing[0];
        else
        {
            var go = new GameObject("PortalCheckRun");
            SceneManager.MoveGameObjectToScene(go, scene);
            context = go.AddComponent<PortalCheckRun>();
        }
        context.SetBuildIdentity(identity);
    }

    public static PortalCheckIdentity ReadIdentity(string check, string expectedCommit, string expectedProject,
        string runId, string output)
    {
        string project = PortalCheckIdentity.CanonicalDirectory(Path.Combine(Application.dataPath, ".."));
        string gitRoot = Git(project, "rev-parse --show-toplevel").Trim();
        string commit = Git(project, "rev-parse --verify HEAD").Trim();
        if (!PortalCheckIdentity.SameDirectory(project, gitRoot)
            || !PortalCheckIdentity.SameDirectory(project, expectedProject) || commit != expectedCommit)
            throw new BuildFailedException("Actual Git SHA/project does not match PORTAL_CHECK identity.");
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output))
            throw new BuildFailedException("PORTAL_CHECK_OUTPUT must be an absolute directory.");
        var identity = new PortalCheckIdentity
        {
            check = check, commit = commit, projectPath = project, runId = runId,
            outputDirectory = PortalCheckIdentity.CanonicalDirectory(output),
            dirty = Git(project, "status --porcelain --untracked-files=normal").Length != 0,
            sourceDigest = SourceDigest(project), unityVersion = Application.unityVersion,
            hdrpVersion = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HDRenderPipeline).Assembly)?.version
        };
        string invalid = identity.Validate();
        if (invalid.Length != 0) throw new BuildFailedException(invalid);
        return identity;
    }

    private static string SourceDigest(string project)
    {
        // Хешируем реальные байты tracked и untracked исходников, включая сгенерированную сцену.
        string[] paths = Git(project, "ls-files -z --cached --others --exclude-standard -- Assets Packages ProjectSettings")
            .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries).Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var manifest = new StringBuilder();
        using (SHA256 hash = SHA256.Create())
        {
            foreach (string path in paths)
            {
                string absolute = Path.Combine(project, path);
                string digest;
                if (File.Exists(absolute))
                {
                    using (FileStream stream = File.OpenRead(absolute)) digest = Hex(hash.ComputeHash(stream));
                }
                else digest = "deleted";
                manifest.Append(path).Append('\0').Append(digest).Append('\n');
            }
            return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString())));
        }
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

    private static string Git(string project, string arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (var process = System.Diagnostics.Process.Start(start))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill();
                throw new BuildFailedException("Git identity query timed out.");
            }
            if (process.ExitCode != 0) throw new BuildFailedException("Cannot query actual Git identity.");
            error.GetAwaiter().GetResult();
            return output.GetAwaiter().GetResult();
        }
    }
}
