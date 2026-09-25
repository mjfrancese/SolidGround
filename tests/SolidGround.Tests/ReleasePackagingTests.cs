using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SolidGround.Tests;

/// <summary>
/// Offline, deterministic, platform-independent checks for SolidGround Issue #17's release packaging,
/// signing, and installer surface (design-record.md §7 "Tests and offline verification," amended by the
/// orchestrator's rulings R12 and R17): the repository-root <c>THIRD-PARTY-NOTICES</c> file, the signing
/// certificate's committed, machine-readable pin file (<c>scripts/signing-certificate.json</c>, ruling
/// R12 -- superseding this design record's earlier "no separate file" plan), and the existence and
/// required content markers of every new <c>scripts/*.ps1</c> file and <c>scripts/install.cmd</c>. These
/// tests read plain text and JSON only and never shell out to PowerShell (ruling R17, and this project's
/// own standing rule already stated in design-record.md §7: no xUnit test may ever start a PowerShell
/// process, since the self-hosted CI runner is Linux) -- matching RevitHostFilesTests.cs's own
/// established discipline for the same reason. This suite therefore never loads <c>SolidGround.Revit.dll</c>
/// and never references <c>SolidGround.Revit</c> from this test project.
/// </summary>
public sealed class ReleasePackagingTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ScriptsDirectory = Path.Combine(RepositoryRoot, "scripts");
    private static readonly string ThirdPartyNoticesPath = Path.Combine(RepositoryRoot, "THIRD-PARTY-NOTICES");
    private static readonly string RevitDefaultLockFilePath = Path.Combine(RepositoryRoot, "src", "SolidGround.Revit", "packages.lock.json");
    private static readonly string SigningCertificatePinFilePath = Path.Combine(ScriptsDirectory, "signing-certificate.json");
    private static readonly string NewReleasePackageScriptPath = Path.Combine(ScriptsDirectory, "New-ReleasePackage.ps1");
    private static readonly string InstallCmdPath = Path.Combine(ScriptsDirectory, "install.cmd");
    private static readonly string InstallGuidePath = Path.Combine(RepositoryRoot, "docs", "revit-install-guide.md");

    // ------------------------------------------------------------------------------------------------
    // (a) THIRD-PARTY-NOTICES (repository root)
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void ThirdPartyNoticesFileExistsAndNamesBothBundledPackages()
    {
        Assert.True(File.Exists(ThirdPartyNoticesPath), $"Missing file: {ThirdPartyNoticesPath}");
        string content = File.ReadAllText(ThirdPartyNoticesPath);

        Assert.Contains("NetTopologySuite", content, StringComparison.Ordinal);
        Assert.Contains("BSD-3-Clause", content, StringComparison.Ordinal);
        Assert.Contains("ProjNET", content, StringComparison.Ordinal);
        Assert.Contains("LGPL", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ThirdPartyNoticesFileNamesEveryPackageInTheLockFileWithItsExactVersion()
    {
        // Self-updating drift guard (design-record.md §7, Draft 4 verifier finding
        // "third-party-notices-omits-project-type-filter"): src/SolidGround.Revit/packages.lock.json's
        // "default" (non-CI) lock file lists solidground.core as "type": "Project" -- SolidGround's own
        // project reference, never a third-party package, and never something THIRD-PARTY-NOTICES could
        // ever name -- so that entry is excluded before checking anything else. Every remaining
        // Direct/Transitive package must be named in THIRD-PARTY-NOTICES together with its exact resolved
        // version (not merely somewhere in the file), so this fails closed the moment a future
        // third-party dependency is added, or an existing one is bumped, without a matching notices
        // update -- rather than only ever re-checking today's fixed NetTopologySuite 2.6.0/ProjNET 2.1.0
        // pair.
        Assert.True(File.Exists(ThirdPartyNoticesPath), $"Missing file: {ThirdPartyNoticesPath}");
        string content = File.ReadAllText(ThirdPartyNoticesPath);
        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        (string Name, string Type, string? Version)[] thirdPartyPackages = [.. ReadLockFilePackages(RevitDefaultLockFilePath)
            .Where(package => !string.Equals(package.Type, "Project", StringComparison.Ordinal))];

        Assert.NotEmpty(thirdPartyPackages);

        List<string> unnamed = [];
        foreach ((string name, _, string? version) in thirdPartyPackages)
        {
            Assert.False(string.IsNullOrWhiteSpace(version), $"Lock file package '{name}' in '{RevitDefaultLockFilePath}' has no resolved version.");

            bool namedWithExactVersion = lines.Any(line =>
                line.Contains(name, StringComparison.Ordinal) && line.Contains(version!, StringComparison.Ordinal));

            if (!namedWithExactVersion)
            {
                unnamed.Add($"{name} {version}");
            }
        }

        Assert.True(
            unnamed.Count == 0,
            $"'{ThirdPartyNoticesPath}' does not name every non-Project-type package from " +
            $"'{RevitDefaultLockFilePath}' together with its exact resolved version on the same line. " +
            $"Missing: {string.Join(", ", unnamed)}.");
    }

    // ------------------------------------------------------------------------------------------------
    // (b) scripts/signing-certificate.json -- the committed, machine-readable certificate pin file
    // (Issue #17 orchestrator ruling R12). This supersedes design-record.md §3's "Where the certificate
    // lives" plan (recovering the certificate live from a signed file's own Authenticode signature, with
    // no separate file); the pin file is now the one source every script checks a signer's identity
    // against, and no script parses a markdown document for it.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SigningCertificatePinFileExistsParsesAndPinsANotYetExpiredUppercaseSha256Hash()
    {
        // The orchestrator mints the certificate -- and therefore this file -- separately via
        // Sign-RevitAddIn.ps1's own -NewCertificate mode (ruling R12); this implementation stage
        // deliberately does not create it or run that mode. Until the orchestrator has minted it, this
        // test is expected to fail, with the explicit, actionable message below rather than a raw
        // FileNotFoundException or JSON parse exception.
        Assert.True(
            File.Exists(SigningCertificatePinFilePath),
            $"Missing pin file: {SigningCertificatePinFilePath}. Issue #17 ruling R12: this file is " +
            "minted by the orchestrator's own 'scripts/Sign-RevitAddIn.ps1 -NewCertificate' run, never by " +
            "this test suite or its implementer -- this failure is expected until that mint step has run.");

        using FileStream stream = File.OpenRead(SigningCertificatePinFilePath);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        string subject = RequireNonEmptyStringProperty(root, "subject");
        string sha256 = RequireNonEmptyStringProperty(root, "sha256");
        string thumbprint = RequireNonEmptyStringProperty(root, "thumbprint");
        string notBefore = RequireNonEmptyStringProperty(root, "notBefore");
        string notAfter = RequireNonEmptyStringProperty(root, "notAfter");

        Assert.Contains("SolidGround", subject, StringComparison.Ordinal);

        // R12: "sha256 (uppercase hex SHA-256 of the certificate DER)".
        Assert.True(
            Regex.IsMatch(sha256, "^[0-9A-F]{64}$"),
            $"'sha256' value '{sha256}' must be exactly 64 uppercase hexadecimal characters.");

        // R12: "thumbprint (SHA-1, informational only)" -- X509Certificate2.Thumbprint is always a
        // 20-byte/40-hex-character SHA-1 digest of the DER-encoded certificate.
        Assert.True(
            Regex.IsMatch(thumbprint, "^[0-9A-F]{40}$"),
            $"'thumbprint' value '{thumbprint}' must be exactly 40 uppercase hexadecimal characters.");

        Assert.True(
            DateTimeOffset.TryParse(notBefore, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset notBeforeValue),
            $"'notBefore' value '{notBefore}' does not parse as an ISO 8601 date/time.");
        Assert.True(
            DateTimeOffset.TryParse(notAfter, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset notAfterValue),
            $"'notAfter' value '{notAfter}' does not parse as an ISO 8601 date/time.");

        Assert.True(notBeforeValue <= notAfterValue, $"'notBefore' ({notBefore}) must not be after 'notAfter' ({notAfter}).");
        Assert.True(notAfterValue > DateTimeOffset.UtcNow, $"'notAfter' value '{notAfter}' must be in the future.");
    }

    // ------------------------------------------------------------------------------------------------
    // (c) scripts/*.ps1 and scripts/install.cmd -- existence and required text markers only. No test in
    // this file (or this project, per R17) ever executes PowerShell or the batch file.
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("New-ReleasePackage.ps1")]
    [InlineData("Sign-RevitAddIn.ps1")]
    [InlineData("Import-SigningTrust.ps1")]
    [InlineData("Install-SolidGround.ps1")]
    [InlineData("Uninstall-SolidGround.ps1")]
    public void ReleaseScriptExistsAndDeclaresASynopsisInItsCommentBasedHelpBlock(string fileName)
    {
        string path = Path.Combine(ScriptsDirectory, fileName);
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        // Matches Deploy-RevitAddIn.ps1's own existing comment-based-help convention; a coarse but
        // falsifiable guard, not a full comment-based-help parser.
        Assert.Contains(".SYNOPSIS", content, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallCmdExistsUnderScriptsAndForwardsToInstallSolidGroundWithExecutionPolicyBypass()
    {
        // Repository source location is scripts/install.cmd, not the repository root (design-record.md
        // §5, Draft 4 finding "install-cmd-repository-location-unstated"): RevitHostFilesTests.cs's own
        // EnumerateFilesToScanForHardcodedPaths scans ScriptsDirectory recursively but never the
        // repository root, so only scripts/install.cmd stays covered by
        // NoRevitProjectOrScriptFileHardcodesAnAllUserAddInPath with zero test changes elsewhere.
        Assert.True(File.Exists(InstallCmdPath), $"Missing file: {InstallCmdPath}");
        string content = File.ReadAllText(InstallCmdPath);

        Assert.Contains("@echo off", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-ExecutionPolicy Bypass", content, StringComparison.Ordinal);
        Assert.Contains("Install-SolidGround.ps1", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sign-RevitAddIn.ps1")]
    [InlineData("Import-SigningTrust.ps1")]
    public void SigningScriptsNeverHardcodeAPfxPathOrPassword(string fileName)
    {
        // D1's own rationale (design-record.md §3 "Secrets handling"): the signing key is
        // -KeyExportPolicy NonExportable and lives only in Cert:\CurrentUser\My, so this design has no
        // .pfx file and no password to protect anywhere. This is a regression guardrail specific to that
        // rationale, not a general secrets scan: it fails loudly if a future edit ever reintroduces the
        // exportable-key/password pattern D1 deliberately avoided.
        //
        // The ".pfx" check looks only for a quoted path/filename ending in ".pfx" (the shape an actual
        // hardcoded value would take: "...signing.pfx" or '...cert.pfx'), not the bare substring --
        // Sign-RevitAddIn.ps1's own comment-based help legitimately documents this design decision in
        // prose ("This script never creates a .pfx file..."), and a literal substring ban would fail that
        // correct, already-written file for explaining itself clearly.
        string path = Path.Combine(ScriptsDirectory, fileName);
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        Assert.False(
            Regex.IsMatch(content, "\\.pfx[\"']", RegexOptions.IgnoreCase),
            $"'{path}' appears to hardcode a quoted .pfx path/filename.");
        Assert.DoesNotContain("-Password", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConvertTo-SecureString", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleasePackagingScriptReusesTheProvenDeployScriptAndNeverRestoresUnderTheCiCondition()
    {
        // design-record.md §4 "Preconditions": New-ReleasePackage.ps1 must reuse, never reimplement, the
        // clean-tree check, --locked-mode restore, dotnet test, and Deploy-RevitAddIn.ps1's own -WhatIf
        // validation -- and must never pass -p:UseRevitReferenceAssemblies=true, which is CI-only (§4
        // precondition 3: "the packaging script must never pass it").
        Assert.True(File.Exists(NewReleasePackageScriptPath), $"Missing file: {NewReleasePackageScriptPath}");
        string content = File.ReadAllText(NewReleasePackageScriptPath);

        Assert.Contains("git status --porcelain", content, StringComparison.Ordinal);
        Assert.Contains("--locked-mode", content, StringComparison.Ordinal);
        Assert.Contains("dotnet test", content, StringComparison.Ordinal);
        Assert.Contains("Deploy-RevitAddIn.ps1", content, StringComparison.Ordinal);

        // Quoted-appearance check, not a bare substring ban: this script's own comment-based help
        // legitimately documents precondition 3 in prose ("never -p:UseRevitReferenceAssemblies=true --
        // that flag is CI-only..."), matching SigningScriptsNeverHardcodeAPfxPathOrPassword's own
        // rationale above for why a literal substring ban would fail a correct, already-written file for
        // explaining itself clearly. An actual PowerShell argument would be a quoted -ArgumentList entry
        // (see this file's own Invoke-CheckedProcess call sites), so only that shape is disallowed here.
        Assert.False(
            Regex.IsMatch(content, "[\"']-p:UseRevitReferenceAssemblies", RegexOptions.IgnoreCase),
            $"'{NewReleasePackageScriptPath}' appears to actually pass -p:UseRevitReferenceAssemblies as an argument.");
    }

    [Fact]
    public void ReleasePackagingScriptBuildsWithContinuousIntegrationBuildTrueToAvoidEmbeddingLocalPaths()
    {
        // Issue #17 dry-run defect fix (docs/architecture/revit-release-packaging-and-signing.md,
        // "Preconditions," step 6): Directory.Build.props only turns <ContinuousIntegrationBuild> on
        // automatically under CI ('$(CI)' == 'true'), so a local packaging run must pass
        // -p:ContinuousIntegrationBuild=true explicitly, or the .NET SDK embeds the packaging
        // operator's own real build path (including their Windows account name) into
        // SolidGround.Revit.dll/.pdb and SolidGround.Core.dll/.pdb instead of mapping it to /_/. This
        // is a static, file-content-only assertion (no PowerShell execution, per ruling R8/R17): it
        // confirms the source text actually passes the property as a quoted argument, the same shape
        // ReleasePackagingScriptReusesTheProvenDeployScriptAndNeverRestoresUnderTheCiCondition already
        // uses to confirm -p:UseRevitReferenceAssemblies is never passed.
        Assert.True(File.Exists(NewReleasePackageScriptPath), $"Missing file: {NewReleasePackageScriptPath}");
        string content = File.ReadAllText(NewReleasePackageScriptPath);

        Assert.True(
            Regex.IsMatch(content, "[\"']-p:ContinuousIntegrationBuild=true[\"']", RegexOptions.IgnoreCase),
            $"'{NewReleasePackageScriptPath}' must pass -p:ContinuousIntegrationBuild=true as an argument to its dotnet build invocation.");
    }

    [Fact]
    public void ReleasePackagingScriptScansStagedFilesForBuildMachineLocalPathsBeforeSigning()
    {
        // Issue #17 dry-run defect fix: the fail-closed backstop for the flag asserted above --
        // New-ReleasePackage.ps1 must scan every staged file for the packaging machine's own
        // $env:USERPROFILE, the repository's absolute root, and '\Users\' plus $env:USERNAME (in both
        // UTF-8/ASCII and UTF-16LE decodings) after staging and before signing/zipping, refusing to
        // package if any staged file still contains one. Static, file-content-only assertion.
        Assert.True(File.Exists(NewReleasePackageScriptPath), $"Missing file: {NewReleasePackageScriptPath}");
        string content = File.ReadAllText(NewReleasePackageScriptPath);

        Assert.Contains("function Test-StagedFilesForLocalMachinePaths", content, StringComparison.Ordinal);
        Assert.Contains("$env:USERPROFILE", content, StringComparison.Ordinal);
        Assert.Contains("$env:USERNAME", content, StringComparison.Ordinal);
        Assert.Contains("[System.Text.Encoding]::UTF8", content, StringComparison.Ordinal);
        Assert.Contains("[System.Text.Encoding]::Unicode", content, StringComparison.Ordinal);

        // The scan call site must appear textually after the payload-staging copy loop and before the
        // signing block, matching the design's "after staging and before signing/zipping" ordering.
        int scanCallIndex = content.IndexOf("Test-StagedFilesForLocalMachinePaths -StagingRoot", StringComparison.Ordinal);
        int payloadCopyIndex = content.IndexOf("Cannot stage release payload:", StringComparison.Ordinal);
        int signingBlockIndex = content.IndexOf("Precondition 10 + 10a: sign", StringComparison.Ordinal);

        Assert.True(scanCallIndex >= 0, $"'{NewReleasePackageScriptPath}' does not call Test-StagedFilesForLocalMachinePaths.");
        Assert.True(payloadCopyIndex >= 0, $"'{NewReleasePackageScriptPath}' is missing its expected payload-staging copy loop marker.");
        Assert.True(signingBlockIndex >= 0, $"'{NewReleasePackageScriptPath}' is missing its expected signing-block marker.");
        Assert.True(payloadCopyIndex < scanCallIndex, "The local-machine-path scan must run after payload staging.");
        Assert.True(scanCallIndex < signingBlockIndex, "The local-machine-path scan must run before signing.");
    }

    [Fact]
    public void InstallGuideExistsAndCoversEveryOperatorTopic()
    {
        // Coarse but falsifiable (design-record.md §7): a guard against silently dropping a whole topic
        // from docs/revit-install-guide.md later, not an exact-heading match against that document's own
        // prose (which this file does not own). Every marker below is a proper noun, file/script name, or
        // exact button-label phrase §6.2's outline names directly, so a faithful guide should contain it
        // verbatim regardless of how that document's own section headings are worded.
        Assert.True(File.Exists(InstallGuidePath), $"Missing file: {InstallGuidePath}");
        string content = File.ReadAllText(InstallGuidePath);

        string[] requiredMarkers =
        [
            "SHA256SUMS",
            "install.cmd",
            "SmartScreen",
            "Import-SigningTrust",
            "OPENTOPOGRAPHY_API_KEY",
            "settings.json",
            "Logs",
            "Load Once",
            "Do Not Load",
            "Troubleshoot",
            "Uninstall",
            "Upgrad",
            "Accuracy",
        ];

        string[] missingMarkers = [.. requiredMarkers.Where(marker => !content.Contains(marker, StringComparison.OrdinalIgnoreCase))];
        Assert.True(
            missingMarkers.Length == 0,
            $"'{InstallGuidePath}' is missing expected operator-topic marker(s): {string.Join(", ", missingMarkers)}.");
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SolidGround.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not locate SolidGround.slnx by walking up from '{AppContext.BaseDirectory}'.");
    }

    private static string RequireNonEmptyStringProperty(JsonElement root, string propertyName)
    {
        Assert.True(root.TryGetProperty(propertyName, out JsonElement property), $"Pin file is missing required property '{propertyName}'.");
        string? value = property.GetString();
        Assert.False(string.IsNullOrWhiteSpace(value), $"Pin file property '{propertyName}' must be a non-empty string.");
        return value!;
    }

    /// <summary>
    /// Parses a NuGet <c>packages*.lock.json</c> file and returns every package name (from every restore
    /// target) paired with its dependency <c>type</c> (<c>"Direct"</c>, <c>"Transitive"</c>, or
    /// <c>"Project"</c>) and resolved version. Mirrors RevitHostFilesTests.ReadLockFilePackages's own
    /// discipline of extracting plain strings inside the method's <c>using</c> scope rather than yielding
    /// <see cref="JsonElement"/>/<see cref="JsonProperty"/> values, which become invalid once their
    /// backing <see cref="JsonDocument"/> is disposed; extended with the <c>type</c> field this suite also
    /// needs, to exclude SolidGround's own <c>"Project"</c>-type self-reference.
    /// </summary>
    private static List<(string Name, string Type, string? Version)> ReadLockFilePackages(string path)
    {
        Assert.True(File.Exists(path), $"Missing lock file: {path}");

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);

        List<(string Name, string Type, string? Version)> packages = [];
        foreach (JsonProperty target in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (JsonProperty package in target.Value.EnumerateObject())
            {
                string type = package.Value.TryGetProperty("type", out JsonElement typeElement)
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;
                string? resolved = package.Value.TryGetProperty("resolved", out JsonElement resolvedElement)
                    ? resolvedElement.GetString()
                    : null;
                packages.Add((package.Name, type, resolved));
            }
        }

        return packages;
    }
}
