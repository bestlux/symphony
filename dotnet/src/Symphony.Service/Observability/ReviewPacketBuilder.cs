using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Symphony.Abstractions.Issues;
using Symphony.Service.Hosting;

namespace Symphony.Service.Observability;

public static partial class ReviewPacketBuilder
{
    private const string WorkpadMarker = "## Codex Workpad";

    public static ReviewPacketPayload Build(
        Issue issue,
        RunningSession? running,
        CompletedRunEntry? completed)
    {
        var workpads = (issue.Comments ?? [])
            .Where(comment => comment.Body?.Contains(WorkpadMarker, StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(comment => comment.UpdatedAt ?? comment.CreatedAt ?? DateTimeOffset.MinValue)
            .ToArray();
        var workpadStatus = WorkpadStatus(workpads);

        var sources = new List<string?>();
        sources.Add(issue.Description);
        sources.AddRange((issue.Comments ?? []).Select(comment => comment.Body));
        sources.AddRange((issue.Links ?? []).Select(link => string.IsNullOrWhiteSpace(link.Url) ? null : $"{link.Title ?? "Link"}: {link.Url}"));
        sources.Add(running?.LastMessage);
        sources.Add(completed?.LastMessage);
        sources.Add(completed?.Error);

        var raw = string.Join(
            Environment.NewLine + Environment.NewLine,
            sources.Where(source => !string.IsNullOrWhiteSpace(source)).Select(source => source!.Trim()));

        var parsed = ParsePacketText(raw);
        parsed.Artifact.AddRange(ArtifactLines(issue, running, completed));
        parsed.Files.AddRange(ChangedFilesFromWorkspaceStatus(completed?.WorkspaceStatus ?? running?.WorkspaceStatus));
        parsed.Links.AddRange(ExtractUrls(raw));

        var prUrl = FirstPrUrl(parsed.Links.Concat(parsed.Artifact));
        var missing = MissingFields(parsed, prUrl, workpadStatus).ToArray();

        if (workpads.Length > 1)
        {
            parsed.Risks.Add($"Multiple active Codex workpad comments found: {workpads.Length}.");
        }

        return new ReviewPacketPayload(
            Summary: Dedupe(parsed.Summary),
            Files: Dedupe(parsed.Files),
            Validation: Dedupe(parsed.Validation),
            Links: Dedupe(parsed.Links),
            Risks: Dedupe(parsed.Risks),
            FollowUps: Dedupe(parsed.FollowUps),
            Artifact: Dedupe(parsed.Artifact),
            PrUrl: prUrl,
            WorkpadStatus: workpadStatus,
            ReadyForHumanReview: missing.Length == 0,
            Missing: missing,
            Raw: raw);
    }

    private static ParsedPacket ParsePacketText(string text)
    {
        var packet = new ParsedPacket();
        PacketSection? current = null;

        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line == "---" || line.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("- [ ]", StringComparison.Ordinal)
                || line.StartsWith("* [ ]", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryReadHeading(line, out var section, out var inline))
            {
                current = section;
                AddLine(packet, section, inline);
                continue;
            }

            var cleaned = CleanPacketLine(line);
            if (cleaned.Length == 0 || IsWorkpadBoilerplate(cleaned))
            {
                continue;
            }

            var inferred = InferSection(cleaned);
            if (inferred is not null)
            {
                AddLine(packet, inferred.Value, cleaned);
            }
            else if (current is not null)
            {
                AddLine(packet, current.Value, cleaned);
            }
        }

        return packet;
    }

    private static bool TryReadHeading(string line, out PacketSection section, out string? inline)
    {
        inline = null;
        section = PacketSection.Summary;

        var isMarkdownHeading = line.TrimStart().StartsWith("#", StringComparison.Ordinal);
        var normalized = line
            .Trim()
            .Trim('*')
            .Trim();
        normalized = HeadingPrefixPattern().Replace(normalized, "");

        var match = HeadingWithInlinePattern().Match(normalized);
        if (!match.Success && !isMarkdownHeading)
        {
            return false;
        }

        var label = (match.Success ? match.Groups["label"].Value : normalized).Trim().ToLowerInvariant();
        inline = match.Success ? match.Groups["inline"].Value.Trim() : null;

        if (label.Contains("changed file", StringComparison.Ordinal) || label == "files")
        {
            section = PacketSection.Files;
            return true;
        }

        if (label.Contains("validation", StringComparison.Ordinal)
            || label.Contains("test", StringComparison.Ordinal)
            || label.Contains("evidence", StringComparison.Ordinal))
        {
            section = PacketSection.Validation;
            return true;
        }

        if (label.Contains("artifact", StringComparison.Ordinal)
            || label.Contains("workspace", StringComparison.Ordinal)
            || label.Contains("branch", StringComparison.Ordinal)
            || label.Contains("pr url", StringComparison.Ordinal)
            || label.Contains("pull request", StringComparison.Ordinal)
            || label == "pr")
        {
            section = PacketSection.Artifact;
            return true;
        }

        if (label.Contains("risk", StringComparison.Ordinal) || label.Contains("blocker", StringComparison.Ordinal))
        {
            section = PacketSection.Risks;
            return true;
        }

        if (label.Contains("follow", StringComparison.Ordinal))
        {
            section = PacketSection.FollowUps;
            return true;
        }

        if (label.Contains("link", StringComparison.Ordinal))
        {
            section = PacketSection.Links;
            return true;
        }

        if (label.Contains("summary", StringComparison.Ordinal) || label.Contains("tl;dr", StringComparison.Ordinal))
        {
            section = PacketSection.Summary;
            return true;
        }

        return false;
    }

    private static PacketSection? InferSection(string line)
    {
        var lower = line.ToLowerInvariant();
        if (FilePathPattern().IsMatch(line) || lower.Contains("files changed", StringComparison.Ordinal))
        {
            return PacketSection.Files;
        }

        if (lower.Contains(".\\scripts\\validate-symphony.ps1", StringComparison.Ordinal)
            || lower.Contains("dotnet test", StringComparison.Ordinal)
            || lower.Contains("npm run", StringComparison.Ordinal)
            || lower.Contains("passed", StringComparison.Ordinal)
            || lower.Contains("failed", StringComparison.Ordinal))
        {
            return PacketSection.Validation;
        }

        if (lower.Contains("github.com/", StringComparison.Ordinal)
            || lower.Contains("branch", StringComparison.Ordinal)
            || lower.Contains("commit", StringComparison.Ordinal)
            || lower.Contains("workspace", StringComparison.Ordinal))
        {
            return PacketSection.Artifact;
        }

        if (lower.Contains("risk", StringComparison.Ordinal) || lower.Contains("blocker", StringComparison.Ordinal))
        {
            return PacketSection.Risks;
        }

        if (lower.Contains("follow-up", StringComparison.Ordinal) || lower.Contains("follow up", StringComparison.Ordinal))
        {
            return PacketSection.FollowUps;
        }

        return null;
    }

    private static void AddLine(ParsedPacket packet, PacketSection section, string? line)
    {
        var cleaned = CleanPacketLine(line);
        if (cleaned.Length == 0)
        {
            return;
        }

        TargetList(packet, section).Add(cleaned);
    }

    private static List<string> TargetList(ParsedPacket packet, PacketSection section) => section switch
    {
        PacketSection.Files => packet.Files,
        PacketSection.Validation => packet.Validation,
        PacketSection.Links => packet.Links,
        PacketSection.Risks => packet.Risks,
        PacketSection.FollowUps => packet.FollowUps,
        PacketSection.Artifact => packet.Artifact,
        _ => packet.Summary
    };

    private static string CleanPacketLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "";
        }

        var cleaned = line.Trim();
        cleaned = ChecklistPattern().Replace(cleaned, "");
        cleaned = BulletPattern().Replace(cleaned, "");
        cleaned = OrderedListPattern().Replace(cleaned, "");
        cleaned = cleaned.Trim().Trim('`').Trim();
        return cleaned;
    }

    private static bool IsWorkpadBoilerplate(string line)
    {
        return line is "## Codex Workpad"
            or "Plan"
            or "Acceptance Criteria"
            or "Validation"
            or "Notes"
            or "Confusions";
    }

    private static string WorkpadStatus(IReadOnlyList<IssueComment> workpads)
    {
        if (workpads.Count == 0)
        {
            return "missing";
        }

        if (workpads.Count > 1)
        {
            return $"multiple workpads ({workpads.Count})";
        }

        var body = workpads[0].Body ?? "";
        var uncheckedCount = UncheckedChecklistPattern().Matches(body).Count;
        var checkedCount = CheckedChecklistPattern().Matches(body).Count;
        if (uncheckedCount > 0)
        {
            return $"incomplete ({uncheckedCount} unchecked)";
        }

        return checkedCount > 0 ? "complete" : "found (no checklist)";
    }

    private static IEnumerable<string> ArtifactLines(
        Issue issue,
        RunningSession? running,
        CompletedRunEntry? completed)
    {
        if (!string.IsNullOrWhiteSpace(issue.Url))
        {
            yield return $"Linear: {issue.Url}";
        }

        if (!string.IsNullOrWhiteSpace(issue.BranchName))
        {
            yield return $"Branch: {issue.BranchName}";
        }

        foreach (var link in issue.Links ?? [])
        {
            if (!string.IsNullOrWhiteSpace(link.Url))
            {
                yield return $"{link.Title ?? link.SourceType ?? "Link"}: {link.Url}";
            }
        }

        var workspace = running?.WorkspacePath ?? completed?.WorkspacePath;
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            yield return $"Workspace: {workspace}";
        }

        var baseCommit = running?.WorkspaceBaseCommit ?? completed?.WorkspaceBaseCommit;
        if (!string.IsNullOrWhiteSpace(baseCommit))
        {
            yield return $"Workspace base commit: {baseCommit}";
        }

        var baseBranch = running?.WorkspaceBaseBranch ?? completed?.WorkspaceBaseBranch;
        if (!string.IsNullOrWhiteSpace(baseBranch))
        {
            yield return $"Workspace base branch: {baseBranch}";
        }
    }

    private static IEnumerable<string> ChangedFilesFromWorkspaceStatus(string? workspaceStatus)
    {
        if (string.IsNullOrWhiteSpace(workspaceStatus))
        {
            yield break;
        }

        foreach (var rawLine in workspaceStatus.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length <= 3)
            {
                continue;
            }

            var path = line[3..].Trim();
            var renameIndex = path.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (renameIndex >= 0)
            {
                path = path[(renameIndex + 4)..].Trim();
            }

            if (path.Length > 0)
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> MissingFields(ParsedPacket packet, string? prUrl, string workpadStatus)
    {
        if (packet.Summary.Count == 0)
        {
            yield return "summary";
        }

        if (packet.Files.Count == 0)
        {
            yield return "changed files";
        }

        if (packet.Validation.Count == 0)
        {
            yield return "validation evidence";
        }

        if (string.IsNullOrWhiteSpace(prUrl))
        {
            yield return "PR URL";
        }

        if (!string.Equals(workpadStatus, "complete", StringComparison.Ordinal))
        {
            yield return "completed workpad";
        }
    }

    private static IReadOnlyList<string> Dedupe(IEnumerable<string> items)
    {
        return items
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractUrls(string text)
    {
        return UrlPattern()
            .Matches(text)
            .Select(match => match.Value.TrimEnd(')', ',', '.', ';'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FirstPrUrl(IEnumerable<string> values)
    {
        return values.FirstOrDefault(value => PullRequestUrlPattern().IsMatch(value));
    }

    [GeneratedRegex(@"^(\s*[-*]\s+)?\[[ xX]\]\s+")]
    private static partial Regex ChecklistPattern();

    [GeneratedRegex(@"^\s*[-*]\s+")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^\s*\d+\.\s+")]
    private static partial Regex OrderedListPattern();

    [GeneratedRegex(@"(?m)^\s*-\s+\[\s\]")]
    private static partial Regex UncheckedChecklistPattern();

    [GeneratedRegex(@"(?m)^\s*-\s+\[[xX]\]")]
    private static partial Regex CheckedChecklistPattern();

    [GeneratedRegex(@"^#+\s*|^\s*[-*]\s+")]
    private static partial Regex HeadingPrefixPattern();

    [GeneratedRegex(@"^(?<label>[^:]+):\s*(?<inline>.*)$")]
    private static partial Regex HeadingWithInlinePattern();

    [GeneratedRegex(@"^[\w./\\-]+\.(cs|tsx|ts|css|json|slnx|xaml|md|ps1)$", RegexOptions.IgnoreCase)]
    private static partial Regex FilePathPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"github\.com/[^/\s]+/[^/\s]+/pull/\d+", RegexOptions.IgnoreCase)]
    private static partial Regex PullRequestUrlPattern();

    private enum PacketSection
    {
        Summary,
        Files,
        Validation,
        Links,
        Risks,
        FollowUps,
        Artifact
    }

    private sealed class ParsedPacket
    {
        public List<string> Summary { get; } = [];
        public List<string> Files { get; } = [];
        public List<string> Validation { get; } = [];
        public List<string> Links { get; } = [];
        public List<string> Risks { get; } = [];
        public List<string> FollowUps { get; } = [];
        public List<string> Artifact { get; } = [];
    }
}

public sealed record ReviewPacketPayload(
    [property: JsonPropertyName("summary")] IReadOnlyList<string> Summary,
    [property: JsonPropertyName("files")] IReadOnlyList<string> Files,
    [property: JsonPropertyName("validation")] IReadOnlyList<string> Validation,
    [property: JsonPropertyName("links")] IReadOnlyList<string> Links,
    [property: JsonPropertyName("risks")] IReadOnlyList<string> Risks,
    [property: JsonPropertyName("followUps")] IReadOnlyList<string> FollowUps,
    [property: JsonPropertyName("artifact")] IReadOnlyList<string> Artifact,
    [property: JsonPropertyName("pr_url")] string? PrUrl,
    [property: JsonPropertyName("workpad_status")] string WorkpadStatus,
    [property: JsonPropertyName("ready_for_human_review")] bool ReadyForHumanReview,
    [property: JsonPropertyName("missing")] IReadOnlyList<string> Missing,
    [property: JsonPropertyName("raw")] string Raw);
