using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SolidGround.Tests;

/// <summary>
/// A pattern-shape guard against personal information re-entering this public repository: US
/// street-address shapes, Windows user-profile paths naming a real user, email addresses outside an
/// explicit allow-list, US ZIP+state prose shapes, and latitude/longitude literal pairs that are not the
/// public example site (see <c>AGENTS.md</c>'s "Test fixture and verification" section). Every detector
/// runs against both each candidate file's content and its own relative path, so a personal string baked
/// into a file or directory name is caught the same way one baked into file content is. The sweep below
/// walks the working tree directly rather than shelling out to <c>git</c>, which a CI runner does not
/// guarantee is available; it skips build output and other directories a checkout never tracks (mirroring
/// <c>.gitignore</c>), and it skips itself, because the tests further down intentionally contain
/// synthetic bad-shaped strings to prove each detector fires.
/// </summary>
public sealed class PersonalInformationGuardTests
{
    // ------------------------------------------------------------------------------------------------
    // Tree-walk configuration
    // ------------------------------------------------------------------------------------------------

    private static readonly string[] SkippedDirectoryNames =
    [
        ".git", "bin", "obj", "artifacts", "TestResults", "coverage", ".vs", ".vscode", ".idea",
        "packages", ".firecrawl", "downloads",
    ];

    // ------------------------------------------------------------------------------------------------
    // 1. US street-address shapes: house number + one to four words + a street suffix.
    // ------------------------------------------------------------------------------------------------

    // Both abbreviated and full-word street-type suffixes are recognized, so a real address written out
    // in full (for example "Example Lane" instead of "Example Ln") is caught by the same detector, not
    // just the short form. Matching stays case-sensitive, exactly as a real address is actually written
    // (Title Case) -- several of these words (Way, Street, Place, Drive, Square, ...) are common lowercase
    // English words too ("either way", "a 40 m square parcel", "turns a street address"), and this
    // repository's own docs use them in ordinary prose; a case-insensitive match on the suffix alone was
    // tried and produces exactly those false positives against this repository's real content.
    private static readonly Regex StreetAddressPattern = new(
        @"(?<!\d)\d{1,6}\s+(?:[A-Za-z][A-Za-z'.-]*\s+){1,4}(?:Street|St|Avenue|Ave|Lane|Ln|Road|Rd|Drive|Dr|Boulevard|Blvd|Court|Ct|Place|Pl|Circle|Cir|Parkway|Pkwy|Terrace|Ter|Highway|Hwy|Trail|Trl|Square|Sq|Way)\b\.?");

    // ------------------------------------------------------------------------------------------------
    // 2. Windows user-profile paths naming a real user.
    // ------------------------------------------------------------------------------------------------

    // Accepts both backslash- and forward-slash-separated forms: a path pasted from a forward-slash
    // context (a URL, a POSIX-style tool, JSON-escaped text using a single slash) must be caught too.
    private static readonly Regex WindowsUserProfilePathPattern = new(
        @"C:[\\/]+Users[\\/]+([^\\/\s""'`]+)",
        RegexOptions.IgnoreCase);

    private static readonly HashSet<string> AllowedWindowsProfilePlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "<user>",
        "[UserName]",
        "%USERNAME%",
        "Public",
        "Default",
    };

    // ------------------------------------------------------------------------------------------------
    // 3. Email addresses outside an explicit allow-list (noreply addresses and example.com).
    // ------------------------------------------------------------------------------------------------

    private static readonly Regex EmailAddressPattern = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b");

    // ------------------------------------------------------------------------------------------------
    // 4. US ZIP+state patterns in prose.
    // ------------------------------------------------------------------------------------------------

    private static readonly string[] UsStateNames =
    [
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware",
        "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky",
        "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi",
        "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico",
        "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania",
        "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont",
        "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming", "District of Columbia",
    ];

    private static readonly string[] UsStateAbbreviations =
    [
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS",
        "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY",
        "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV",
        "WI", "WY", "DC",
    ];

    private static readonly Regex ZipWithFullStateNamePattern = new(
        @"\b(?:" + string.Join("|", UsStateNames.Select(Regex.Escape)) + @")\s+\d{5}(?:-\d{4})?\b");

    // A bare two-letter abbreviation collides with ordinary words ("OR", "IN", "HI", "MO", ...), so this
    // pattern is case-sensitive (the abbreviations are all-uppercase; ordinary prose words are not) and
    // still requires the abbreviation to be immediately followed by whitespace and a 5-digit ZIP shape.
    // A leading comma is not required -- "Faketown NE 68999" (no comma before the state) must be
    // caught just as reliably as "Faketown, NE 68999".
    private static readonly Regex ZipWithStateAbbreviationPattern = new(
        @"\b(?:" + string.Join("|", UsStateAbbreviations) + @")\s+\d{5}(?:-\d{4})?\b");

    // ------------------------------------------------------------------------------------------------
    // 5. Latitude/longitude literal pairs that are not the public example site.
    // ------------------------------------------------------------------------------------------------

    // The public example site's own coordinate, exactly as recorded in AGENTS.md's "Test fixture and
    // verification" section; hard-coded here so this guard recognizes it without parsing that file.
    private const double PublicSiteLatitude = 41.591194d;
    private const double PublicSiteLongitude = -93.603806d;
    private const double CoordinateToleranceDegrees = 0.01d;

    // Wide enough to span a Markdown table row or a short multi-line JSON block that separates a
    // latitude field from its longitude field with a few other fields in between -- both real shapes a
    // leaked coordinate pair could take in this repository's own docs and fixtures -- while still bounded
    // rather than scanning the whole file. A higher false-positive rate here is an accepted trade-off:
    // AllowedSyntheticCoordinateLiterals and the tolerance check above already give legitimate values an
    // escape hatch.
    private const int CoordinatePairProximityCharacters = 500;

    // AGENTS.md scopes this project to the conterminous United States (the supported UTM zones are
    // 10N-19N, with the one zone reaching outside CONUS -- 19N's slice of Puerto Rico -- carved out by
    // latitude). These bounds are deliberately tighter than a generic +/-90 / +/-180 latitude/longitude
    // range: a generic range also matches this repository's own local-frame point coordinates (a parcel
    // is tens of meters wide) and unrelated small constants (unit-conversion factors, Revit's own
    // hardcoded default SiteLocation in radians), none of which is a location at all.
    private const double MinimumConusLikeLatitude = 20d;
    private const double MaximumConusLikeLatitude = 50d;
    private const double MinimumConusLikeLongitude = -130d;
    private const double MaximumConusLikeLongitude = -60d;

    // Values shaped like a latitude or longitude that are known, reviewed, and not a location -- each
    // needs a reason. Kept as a second line of defense (the CONUS-like range above already excludes this
    // one) and as a worked example for whoever needs to add the next one.
    private static readonly HashSet<string> AllowedSyntheticCoordinateLiterals = new(StringComparer.Ordinal)
    {
        // Revit's own hardcoded default ActiveProjectLocation.SiteLocation before any project positioning
        // is set (Boston, MA, in radians), captured verbatim in a probe log -- see
        // docs/architecture/revit-toposolid-creation.md's orphan-check step. Revit product trivia, not
        // the repository owner's location.
        "0.7392981125588769",
        "-1.2401740653673199",

        // CliProcessCommandTests.RejectsAnEmptyClipWithASourceQualityExitCode's deliberately offset bbox
        // latitude bounds: "~5.5 km north of the fixture grid", per that test's own comment -- still in
        // Iowa, nowhere near the repository owner's real location, and outside this guard's own
        // CoordinateToleranceDegrees of the public example site only because the test deliberately needs
        // a bbox with no data at that latitude, not because it is sensitive.
        "41.6405",
        "41.6419",
    };

    // Four or more fractional digits (roughly 11 m of precision at these latitudes) is enough to identify
    // a specific residential lot, so the minimum is four, not five: a coordinate rounded to four decimal
    // places is still sensitive and must not slip past this detector untested.
    private static readonly Regex CoordinateLiteralPattern = new(@"(?<!\d)-?\d{1,3}\.\d{4,}(?!\d)");

    private readonly record struct CoordinateCandidate(Match RegexMatch, double Value, bool IsLatitudeLike);

    // ------------------------------------------------------------------------------------------------
    // The sweep
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void CommittedTrackedFilesContainNoPersonalInformationShapes()
    {
        string repositoryRoot = FindRepositoryRoot();
        List<string> violations = [];

        foreach (string path in EnumerateCandidateTextFiles(repositoryRoot))
        {
            string relativePath = Path.GetRelativePath(repositoryRoot, path);

            // The file/directory name itself is a candidate, independent of whether its content is text
            // or binary: a personal place name baked directly into a fixture's file name must be caught
            // even though the guard below only ever reads bytes.
            foreach (string violation in DescribeViolations(relativePath))
            {
                violations.Add($"{relativePath}: {violation} (in the file/directory name)");
            }

            string? content = TryReadAsText(path);
            if (content is null)
            {
                continue;
            }

            foreach (string violation in DescribeViolations(content))
            {
                violations.Add($"{relativePath}: {violation}");
            }
        }

        Assert.True(violations.Count == 0, BuildFailureMessage(violations));
    }

    // ------------------------------------------------------------------------------------------------
    // Synthetic-string proofs: each detector below is exercised directly, on strings that are never the
    // repository's own old address or old coordinates.
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Please mail the report to 4821 Example St before Friday.")]
    [InlineData("Please mail the report to 4821 Example Ave before Friday.")]
    [InlineData("Please mail the report to 4821 Example Ln before Friday.")]
    [InlineData("Please mail the report to 4821 Example Rd before Friday.")]
    [InlineData("Please mail the report to 4821 Example Dr before Friday.")]
    [InlineData("Please mail the report to 4821 Example Blvd before Friday.")]
    [InlineData("Please mail the report to 4821 Example Ct before Friday.")]
    [InlineData("Please mail the report to 4821 Example Way before Friday.")]
    public void StreetAddressDetectorFiresOnASyntheticHouseNumberAndStreetSuffix(string content)
    {
        Assert.NotEmpty(FindStreetAddressShapes(content));
    }

    [Theory]
    [InlineData("Please mail the report to 4821 Example Street before Friday.")]
    [InlineData("Please mail the report to 4821 Example Avenue before Friday.")]
    [InlineData("Please mail the report to 4821 Example Lane before Friday.")]
    [InlineData("Please mail the report to 4821 Example Road before Friday.")]
    [InlineData("Please mail the report to 4821 Example Drive before Friday.")]
    [InlineData("Please mail the report to 4821 Example Boulevard before Friday.")]
    [InlineData("Please mail the report to 4821 Example Court before Friday.")]
    [InlineData("Please mail the report to 4821 Example Place before Friday.")]
    [InlineData("Please mail the report to 4821 Example Circle before Friday.")]
    [InlineData("Please mail the report to 4821 Example Parkway before Friday.")]
    [InlineData("Please mail the report to 4821 Example Terrace before Friday.")]
    [InlineData("Please mail the report to 4821 Example Highway before Friday.")]
    [InlineData("Please mail the report to 4821 Example Trail before Friday.")]
    [InlineData("Please mail the report to 4821 Example Square before Friday.")]
    public void StreetAddressDetectorFiresOnASyntheticHouseNumberAndFullWordStreetSuffix(string content)
    {
        Assert.NotEmpty(FindStreetAddressShapes(content));
    }

    [Fact]
    public void StreetAddressDetectorIgnoresProseWithNoLeadingHouseNumber()
    {
        string content = "The delivery truck took an unusual route, a fair way from the loading dock.";

        Assert.Empty(FindStreetAddressShapes(content));
    }

    [Fact]
    public void WindowsUserProfilePathDetectorFiresOnASyntheticUserName()
    {
        string content = @"Logs were saved under C:\Users\SomeoneElse\AppData\Local\Temp\solidground-test\.";

        Assert.NotEmpty(FindWindowsUserProfilePaths(content));
    }

    [Fact]
    public void WindowsUserProfilePathDetectorFiresOnASyntheticUserNameWithForwardSlashes()
    {
        string content = "Logs were saved under C:/Users/SomeoneElse/AppData/Local/Temp/solidground-test/.";

        Assert.NotEmpty(FindWindowsUserProfilePaths(content));
    }

    [Theory]
    [InlineData(@"C:\Users\<user>\AppData\Roaming\Autodesk\Revit\Autodesk Revit 2027\Revit.ini")]
    [InlineData(@"C:\Users\[UserName]\AppData\Roaming\Autodesk\Revit\AddIns\2027")]
    [InlineData(@"C:\Users\Public\Documents\readme.txt")]
    [InlineData(@"C:\Users\Default\NTUSER.DAT")]
    public void WindowsUserProfilePathDetectorAllowsDocumentedPlaceholders(string content)
    {
        Assert.Empty(FindWindowsUserProfilePaths(content));
    }

    [Fact]
    public void EmailAddressDetectorFiresOnASyntheticPersonalAddress()
    {
        string content = "Reach the reviewer at reviewer@fictitious-mail.test for questions.";

        Assert.NotEmpty(FindDisallowedEmailAddresses(content));
    }

    [Theory]
    [InlineData("Commits are attributed to noreply@anthropic.com.")]
    [InlineData("Send sample data to person@example.com for a demo.")]
    [InlineData("A GitHub-style noreply address: 12345+octocat@users.noreply.github.com.")]
    public void EmailAddressDetectorAllowsNoReplyAndExampleDotComAddresses(string content)
    {
        Assert.Empty(FindDisallowedEmailAddresses(content));
    }

    [Theory]
    [InlineData("Contact bob@my-noreply-archive.com for the archive.")]
    [InlineData("The spoofed sender was attacker@notreallynoreply.com.")]
    public void EmailAddressDetectorRejectsADomainThatMerelyContainsNoReplyAsASubstring(string content)
    {
        Assert.NotEmpty(FindDisallowedEmailAddresses(content));
    }

    [Fact]
    public void ZipCodeWithStateDetectorFiresOnASyntheticFullStateNameShape()
    {
        string content = "The sample record lists Faketown, Nebraska 68999 as its mailing city.";

        Assert.NotEmpty(FindZipCodeWithStateShapes(content));
    }

    [Fact]
    public void ZipCodeWithStateDetectorFiresOnASyntheticAbbreviationShape()
    {
        string content = "The sample record lists Faketown, NE 68999 as its mailing city.";

        Assert.NotEmpty(FindZipCodeWithStateShapes(content));
    }

    [Fact]
    public void ZipCodeWithStateDetectorFiresOnASyntheticAbbreviationShapeWithNoComma()
    {
        string content = "The sample record lists Faketown NE 68999 as its mailing city.";

        Assert.NotEmpty(FindZipCodeWithStateShapes(content));
    }

    [Fact]
    public void ZipCodeWithStateDetectorIgnoresAStateNameWithNoAdjacentZipCode()
    {
        string content = "Nebraska has no state income tax on this particular filing status.";

        Assert.Empty(FindZipCodeWithStateShapes(content));
    }

    [Fact]
    public void CoordinatePairDetectorFiresOnASyntheticPairFarFromTheExampleSite()
    {
        string content = "A synthetic waypoint at 35.123456, -100.987654 for testing only.";

        Assert.NotEmpty(FindSensitiveCoordinatePairs(content));
    }

    [Fact]
    public void CoordinatePairDetectorFiresOnASyntheticPairWithOnlyFourFractionalDigits()
    {
        string content = "A synthetic waypoint at 35.1235, -100.9877 for testing only.";

        Assert.NotEmpty(FindSensitiveCoordinatePairs(content));
    }

    [Fact]
    public void CoordinatePairDetectorAllowsThePublicExampleSiteCoordinateExactly()
    {
        string content = "The primary offline test scenario is centered at 41.591194, -93.603806.";

        Assert.Empty(FindSensitiveCoordinatePairs(content));
    }

    [Fact]
    public void CoordinatePairDetectorAllowsValuesWithinToleranceOfThePublicExampleSite()
    {
        string content = "... fetch --bbox -93.6045,41.5906,-93.6031,41.5917 --output out --name example-site";

        Assert.Empty(FindSensitiveCoordinatePairs(content));
    }

    [Fact]
    public void CoordinatePairDetectorIgnoresLocalFrameTriplesThatAreNotShapedLikeWorldCoordinates()
    {
        // A local-frame (x, y, elevation) row from a terrain export: small numbers, alternating sign,
        // never realistic latitude/longitude magnitudes.
        string content = "-1.6404166666666669,4.921250000000001,3.936999999999963";

        Assert.Empty(FindSensitiveCoordinatePairs(content));
    }

    [Fact]
    public void CoordinatePairDetectorIgnoresRevitsOwnDefaultSiteLocationRadians()
    {
        string content = "siteLocation=(Lat=0.7392981125588769,Lon=-1.2401740653673199,PlaceName='Boston, MA')";

        Assert.Empty(FindSensitiveCoordinatePairs(content));
    }

    [Fact]
    public void CoordinatePairDetectorRequiresTheTwoNumbersToBeNearEachOther()
    {
        string content = "35.123456" + new string('x', 550) + "-100.987654";

        Assert.Empty(FindSensitiveCoordinatePairs(content));
    }

    [Fact]
    public void CoordinateAllowListAcceptsAnExplicitlyListedSyntheticValueOutsideTheSiteTolerance()
    {
        Assert.True(IsAllowedCoordinateLiteral("0.7392981125588769", 0.7392981125588769d));
        Assert.True(IsAllowedCoordinateLiteral("-1.2401740653673199", -1.2401740653673199d));
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

    private static IEnumerable<string> EnumerateCandidateTextFiles(string repositoryRoot)
    {
        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(repositoryRoot);

        while (pendingDirectories.Count > 0)
        {
            string directory = pendingDirectories.Pop();

            foreach (string subdirectory in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(subdirectory);
                if (!SkippedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    pendingDirectories.Push(subdirectory);
                }
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (!IsSkippedFileName(Path.GetFileName(file)))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsSkippedFileName(string fileName)
    {
        // This file itself intentionally contains synthetic bad-shaped strings, in the string literals
        // exercising the detectors above; it must never be a candidate for its own sweep.
        if (string.Equals(fileName, "PersonalInformationGuardTests.cs", StringComparison.Ordinal))
        {
            return true;
        }

        // Never read secrets, even though they are also untracked and gitignored: AGENTS.md's "Secrets,
        // downloads, and logs" section is explicit that key material must never reach a log or message,
        // and an assertion-failure message is exactly that.
        if (string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "secrets.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, ".env.example", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? TryReadAsText(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // A NUL byte reliably distinguishes binary content (PNG, DLL, ...) from this repository's actual
        // text files, none of which ever contains one.
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static IEnumerable<string> DescribeViolations(string content)
    {
        foreach (string match in FindStreetAddressShapes(content))
        {
            yield return $"street-address shape '{match}'";
        }

        foreach (string match in FindWindowsUserProfilePaths(content))
        {
            yield return $"Windows user-profile path '{match}'";
        }

        foreach (string match in FindDisallowedEmailAddresses(content))
        {
            yield return $"email address '{match}'";
        }

        foreach (string match in FindZipCodeWithStateShapes(content))
        {
            yield return $"ZIP+state shape '{match}'";
        }

        foreach (string match in FindSensitiveCoordinatePairs(content))
        {
            yield return $"coordinate literal '{match}'";
        }
    }

    private static string BuildFailureMessage(List<string> violations)
    {
        const int maxShown = 50;
        string header = "Personal-information-shaped content found in the committed tree:";
        string body = string.Join(Environment.NewLine, violations.Take(maxShown));
        string footer = violations.Count > maxShown
            ? Environment.NewLine + $"...and {violations.Count - maxShown} more."
            : string.Empty;

        return header + Environment.NewLine + body + footer;
    }

    private static string[] FindStreetAddressShapes(string content)
    {
        return [.. StreetAddressPattern.Matches(content).Select(m => m.Value)];
    }

    private static string[] FindWindowsUserProfilePaths(string content)
    {
        List<string> violations = [];
        foreach (Match match in WindowsUserProfilePathPattern.Matches(content))
        {
            string user = match.Groups[1].Value;
            if (!AllowedWindowsProfilePlaceholders.Contains(user))
            {
                violations.Add(match.Value);
            }
        }

        return [.. violations];
    }

    private static string[] FindDisallowedEmailAddresses(string content)
    {
        return
        [
            .. EmailAddressPattern.Matches(content)
                .Select(m => m.Value)
                .Where(email => !IsAllowedEmailAddress(email)),
        ];
    }

    private static bool IsAllowedEmailAddress(string email)
    {
        int at = email.LastIndexOf('@');
        if (at < 0)
        {
            return false;
        }

        string localPart = email[..at];
        string domain = email[(at + 1)..];

        // An exact domain LABEL match, not a substring match: "users.noreply.github.com" has a "noreply"
        // label and is allowed, but "my-noreply-archive.com" and "notreallynoreply.com" merely contain the
        // substring "noreply" within a different label and must not be allowed by it.
        bool isNoReply = localPart.Equals("noreply", StringComparison.OrdinalIgnoreCase)
            || localPart.Equals("no-reply", StringComparison.OrdinalIgnoreCase)
            || domain.Split('.').Any(label => label.Equals("noreply", StringComparison.OrdinalIgnoreCase));

        bool isExampleDomain = domain.Equals("example.com", StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith(".example.com", StringComparison.OrdinalIgnoreCase);

        return isNoReply || isExampleDomain;
    }

    private static string[] FindZipCodeWithStateShapes(string content)
    {
        return
        [
            .. ZipWithFullStateNamePattern.Matches(content).Select(m => m.Value),
            .. ZipWithStateAbbreviationPattern.Matches(content).Select(m => m.Value),
        ];
    }

    private static string[] FindSensitiveCoordinatePairs(string content)
    {
        List<CoordinateCandidate> candidates = [];
        foreach (Match match in CoordinateLiteralPattern.Matches(content))
        {
            double value = double.Parse(match.Value, CultureInfo.InvariantCulture);
            bool isLatitudeLike = IsLatitudeLike(value);
            if (!isLatitudeLike && !IsLongitudeLike(value))
            {
                continue;
            }

            candidates.Add(new CoordinateCandidate(match, value, isLatitudeLike));
        }

        List<string> violations = [];
        for (int i = 0; i + 1 < candidates.Count; i++)
        {
            CoordinateCandidate first = candidates[i];
            CoordinateCandidate second = candidates[i + 1];

            int gap = second.RegexMatch.Index - (first.RegexMatch.Index + first.RegexMatch.Length);
            if (gap > CoordinatePairProximityCharacters || first.IsLatitudeLike == second.IsLatitudeLike)
            {
                continue;
            }

            if (!IsAllowedCoordinateLiteral(first.RegexMatch.Value, first.Value))
            {
                violations.Add(first.RegexMatch.Value);
            }

            if (!IsAllowedCoordinateLiteral(second.RegexMatch.Value, second.Value))
            {
                violations.Add(second.RegexMatch.Value);
            }
        }

        return [.. violations.Distinct(StringComparer.Ordinal)];
    }

    private static bool IsLatitudeLike(double value) => value is >= MinimumConusLikeLatitude and <= MaximumConusLikeLatitude;

    private static bool IsLongitudeLike(double value) => value is >= MinimumConusLikeLongitude and <= MaximumConusLikeLongitude;

    private static bool IsAllowedCoordinateLiteral(string literalText, double value)
    {
        if (AllowedSyntheticCoordinateLiterals.Contains(literalText))
        {
            return true;
        }

        return Math.Abs(value - PublicSiteLatitude) <= CoordinateToleranceDegrees
            || Math.Abs(value - PublicSiteLongitude) <= CoordinateToleranceDegrees;
    }
}
