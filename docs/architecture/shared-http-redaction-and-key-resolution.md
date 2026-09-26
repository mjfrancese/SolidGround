# Shared HTTP redaction and key resolution

Issue #27 (PH3-0) generalizes the OpenTopography-only query-string redaction and API-key resolution logic
into one provider-agnostic `SolidGround.Core.Http` component, so every current and future HTTP source
(OpenTopography today; the PH3-1 Esri geocoder (#28), PH3-2 parcel sources (#29), and PH3-5 CLI wiring (#32)
tomorrow) shares one implementation instead of each hand-rolling its own copy. It adds no package reference:
`System.Text.Json`, `System.Text.RegularExpressions`, and `System.IO` are all already BCL-available to Core.

## Purpose and boundary

This issue moves *mechanism*, not *policy*: it does not add a new provider integration, and it does not
touch `OpenTopographyApiKey` (the concrete type that already exists) or any call site's *observable*
behavior. `SolidGround.Core.Sources.OpenTopography.OpenTopographyRedaction` keeps its exact public
signatures and byte-identical output, now as a thin facade; `EnvironmentOpenTopographyApiKeyProvider` and
`SolidGround.Cli.Secrets.CliOpenTopographyApiKeyProvider` keep their exact public shapes, with their bodies
rewired onto the shared resolver. See "Per-host resolution order" below for the explicit before/after
equivalence for both hosts.

## The five new types

All under `src/SolidGround.Core/Http/`, namespace `SolidGround.Core.Http`:

| Type | File | Purpose |
| --- | --- | --- |
| `ApiKey` | `ApiKey.cs` | Provider-agnostic safe wrapper for a secret value. Direct generalization of `OpenTopographyApiKey`; that type is left independent and unchanged (see "What stays independent" below) -- this one exists for a *future* provider with no pinned key type of its own. `[DebuggerDisplay("[REDACTED]")]`, and `ToString()` always returns `"[REDACTED]"`; its raw value is reachable only through an `internal` property. |
| `SensitiveQueryParameterNames` | `SensitiveQueryParameterNames.cs` | Named presets: `OpenTopography` (`["API_Key"]`), `Esri` (`["token"]`), and `KnownFamilies` (every name known to this codebase so far, `["API_Key", "token"]`) for a provider-agnostic log sink. Matching is always case-insensitive, so a preset never needs both a lower- and upper-case spelling of the same name. |
| `SensitiveQueryRedactor` | `SensitiveQueryRedactor.cs` | `RedactUri(Uri, IReadOnlyCollection<string>)` and `RedactText(string, IReadOnlyCollection<string>, ApiKey?, bool, int)`. Direct generalization of `OpenTopographyRedaction`'s original implementation, parameterized by name instead of hardcoded to `"API_Key"`. `RedactTrailingKeyFragment` and `Truncate` are verbatim copies of `OpenTopographyRedaction`'s original private helpers (the truncation-boundary and self-overlap handling those methods encode is unchanged). |
| `ApiKeyResolver` | `ApiKeyResolver.cs` | `TryResolve(string environmentVariableName, Func<string,string?> getEnvironmentVariable, UserSecretsLocation? userSecrets = null)` returns the trimmed, non-empty value or null; `Resolve(...)` is the same lookup but throws `MissingApiKeyException` instead of returning null. Also declares `UserSecretsLocation(string SecretsId, string KeyName)`, `MissingApiKeyException`, and `UserSecretsResolutionException`. |
| `UserSecretsFileLocator` | `UserSecretsFileLocator.cs` | `internal`. Moved verbatim from `SolidGround.Cli.Secrets.UserSecretsFileLocator`; only `secretsId` became a parameter instead of a hardcoded `"solidground-cli"` constant. `ApiKeyResolver` is the public surface -- this issue adds no `[InternalsVisibleTo]` grant that would let a test call it directly. |

## What stays independent

| Type | Disposition | Why |
| --- | --- | --- |
| `OpenTopographyRedaction` | Kept, rewritten as a thin facade over `SensitiveQueryRedactor`, permanently scoped to `SensitiveQueryParameterNames.OpenTopography` only | Pinned by `OpenTopographyRedactionTests.cs` (18 tests) and 30 call sites in `OpenTopographyUsgs1mSource.cs`. Its facade scope must never widen to `KnownFamilies` -- doing so could change this provider's own output for a response that happened to contain, say, a `token=` substring, which would violate "byte-identical for every input today's code can produce." That specific scope invariant (as opposed to the facade's other behavior) is pinned by a separate file, `OpenTopographyRedactionScopeTests.cs`: a code-review follow-up found that none of `OpenTopographyRedactionTests.cs`'s 18 tests or the other new Issue #27 test files ever exercised a `token=` input against this facade, so a regression that widened its preset argument to `KnownFamilies` would have passed every test silently. |
| `OpenTopographyApiKey` | Untouched, zero edits | Pinned by `ArchitectureTests.OpenTopographyApiKeyExposesNoPublicMemberThatReturnsTheRawKey`, `OpenTopographyUsgs1mSourceTests.cs`'s direct constructor call, and `OpenTopographyRedactionTests.cs`/`OpenTopographyConfigurationTests.cs`. Wrapping it around the new `Http.ApiKey` was considered and rejected: no required benefit for this issue's stated scope ("redaction" and "key resolution", not "unify the key value type"), and unnecessary edit risk to a pinned reflection test. `OpenTopographyRedaction.RedactText` bridges the two types by constructing `new ApiKey(key.RawValue)` -- ordinary `internal` access (same assembly), and it can never throw, because `OpenTopographyApiKey`'s own constructor already rejects a null/blank value before an instance can exist. |
| `EnvironmentOpenTopographyApiKeyProvider`, `StaticOpenTopographyApiKeyProvider`, `IOpenTopographyApiKeyProvider` | Interface and `Static...` untouched; `Environment...`'s method body migrates internally to call `ApiKeyResolver.TryResolve` | Public shape is exactly what `CreateToposolidCommand.cs` (Revit) and `OpenTopographyUsgs1mSourceTests.cs` already depend on. |
| `CliOpenTopographyApiKeyProvider` | Rewritten as a thin adapter (same public constructor/method shape) | `CliSecretsResolutionTests.cs` exercises it only through `FetchCommand`/`RunCommand`/`CliApplication.RunAsync`, never by class name, so its internals were free to change. |

## Per-host resolution order -- before and after (equal)

| Host | Before | After | Equal? |
| --- | --- | --- | --- |
| CLI `fetch`/`run` | 1. `OPENTOPOGRAPHY_API_KEY` via `host.GetEnvironmentVariable`, trimmed; blank/absent falls through. 2. `solidground-cli` user-secrets file, located by the APPDATA to HOME to `ApplicationData` to `UserProfile` to `DOTNET_USER_SECRETS_FALLBACK_DIR` algorithm, top-level string property `OPENTOPOGRAPHY_API_KEY`. | `ApiKeyResolver.TryResolve("OPENTOPOGRAPHY_API_KEY", getEnvironmentVariable, new UserSecretsLocation("solidground-cli", "OPENTOPOGRAPHY_API_KEY"))` -- identical order, file-location algorithm (moved verbatim), and property name. | Yes. |
| Revit (Preflight and fetch-mode acquisition) | `OPENTOPOGRAPHY_API_KEY` via real `Environment.GetEnvironmentVariable`, trimmed; blank/absent -> null. No user-secrets fallback. | `ApiKeyResolver.TryResolve("OPENTOPOGRAPHY_API_KEY", Environment.GetEnvironmentVariable)` -- `userSecrets` omitted, so resolution stops after the environment check, identically. | Yes -- env-only, preserved by passing no `UserSecretsLocation`. |

## A hardening found during implementation: never chain a `JsonException` that can echo file content

`System.Text.Json`'s reader raises `JsonException` with a message that echoes back the *entire* offending
input when the parser takes a leading byte as a failed attempt at the `true`/`false`/`null` literal (for
example, content beginning with `n` that is not `null`). A corrupted user-secrets file whose content
happens to be exactly this shape -- including, in the worst case, a raw key value pasted without its JSON
wrapper -- would put that value inside `JsonException.Message`. `Exception.ToString()` always recurses into
`InnerException`, so chaining that caught `JsonException` (as the pre-Issue-#27 CLI code did) would let a raw
key value reach a logged `.ToString()` even though the wrapping exception's own `Message` never mentions it.
`ApiKeyResolver.TryResolve`'s `JsonException` catch therefore does not chain it: `UserSecretsResolutionException`
carries only the fixed, path-naming message, with `InnerException` left null for this specific catch (the
`IOException`/`UnauthorizedAccessException` catch above it still chains, because a file-access failure's
message never contains file content). This is the same rule
`OpenTopographyUsgs1mSource.AcquireDetailedAsync` already applies to its own network-exception messages --
see `opentopography-usgs1m-source.md`'s "Key transport and redaction" section. No pinned test observes
`UserSecretsResolutionException.InnerException`, so this is not a behavior change for any existing,
untouched test; `ApiKeyResolverTests.NoExceptionMessageEverContainsAConfiguredKeyValue` is the new regression
guard.

## How a future source calls in

A new HTTP source picks an environment-variable name (for example `GEOCODIO_API_KEY`, `ARCGIS_API_KEY`) and,
if it wants a CLI-style user-secrets fallback, a `UserSecretsLocation`. It calls
`ApiKeyResolver.TryResolve`/`Resolve` to get the trimmed string, wraps it in its own key type or the shared
`Http.ApiKey`, and calls `SensitiveQueryRedactor.RedactUri`/`RedactText` with its own
`SensitiveQueryParameterNames` preset (adding a new one here when the provider's sensitive parameter name is
not `API_Key` or `token`) wherever it needs to log a request URI or server-supplied text. No source integration
is added by this issue; PH3-1 (#28), PH3-2 (#29), and PH3-5 (#32) are the first callers.

## Test evidence

New, additive test files under `tests/SolidGround.Tests/` (no existing test file edited):

| File | Covers |
| --- | --- |
| `SensitiveQueryRedactorTests.cs` | `RedactUri`/`RedactText` against the `OpenTopography` and `Esri` presets, an arbitrary caller-supplied name (proving genuine configurability, not a hardcoded provider vocabulary), a free-form log line embedding a URL, the secret's raw/escaped-form replacement using `Http.ApiKey`, and argument validation (null/empty/blank parameter-name sets, null `uri`/`text`). |
| `ApiKeyTests.cs` | `ApiKey`'s constructor validation, `ToString()`, and the same reflection guard `ArchitectureTests.OpenTopographyApiKeyExposesNoPublicMemberThatReturnsTheRawKey` already applies to `OpenTopographyApiKey`. |
| `ApiKeyResolverTests.cs` | Environment-first-then-user-secrets order, blank/missing fallthrough, no-user-secrets-location (Revit's shape) never consulting a real file on disk, malformed-JSON and non-string-property error naming only the path, the APPDATA/HOME path algorithm through the public `TryResolve` surface, the throwing `Resolve` overload's zero-HTTP-calls contract, and the "no exception message ever contains a configured key value" regression guard described above. |
| `CliOpenTopographyApiKeyProviderMigrationTests.cs` | End-to-end through `CliApplication.RunAsync`, re-proving the CLI's env-wins-over-secrets order, fail-closed-with-no-HTTP-request behavior, and malformed-secrets-file usage error are unchanged after migration -- belt-and-suspenders alongside the untouched `CliSecretsResolutionTests.cs`. |

All new tests use an injected `Func<string,string?>` (a `Dictionary<string,string?>`-backed fake, mirroring
`CliSecretsResolutionTests.cs`'s existing idiom), so none of them touch the real process environment or need
to join the `OpenTopographyEnvironmentCollectionDefinition` collection.
