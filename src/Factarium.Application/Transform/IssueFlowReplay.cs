using System.Globalization;

namespace Factarium.Application.Transform;

/// <summary>
/// One item within a changelog history entry (the shape Jira returns): a field that changed
/// and its before/after values (ids in <see cref="From"/>/<see cref="To"/>, human-readable in
/// <see cref="FromString"/>/<see cref="ToString"/>).
/// </summary>
public sealed record ChangelogItem(string Field, string? FieldId, string? From, string? FromString, string? To, string? ToStr);

/// <summary>One changelog history entry: who changed what, and when.</summary>
public sealed record ChangelogEntry(DateTimeOffset Created, string? AuthorAccountId, string? AuthorDisplayName, IReadOnlyList<ChangelogItem> Items);

/// <summary>Everything the replay needs for one issue.</summary>
public sealed record IssueFlowInput(
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? CurrentStatus,
    string? CurrentAssigneeAccountId,
    IReadOnlyList<ChangelogEntry> Entries,
    DateTimeOffset Now);

/// <summary>A contiguous span in one status, attributed to the assignee at the time.</summary>
public sealed record StatusSegment(string Status, string? AssigneeAccountId, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>A contiguous span the issue was flagged (blocked), attributed to the assignee.</summary>
public sealed record FlaggedSegment(string? AssigneeAccountId, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>An interval an issue belonged to a sprint (RemovedAt null = still in it).</summary>
public sealed record SprintMembershipSpan(long SprintId, string? SprintName, DateTimeOffset AddedAt, DateTimeOffset? RemovedAt);

/// <summary>The full flow reconstruction for one issue.</summary>
public sealed record IssueFlow(
    IReadOnlyList<StatusSegment> StatusSegments,
    IReadOnlyList<FlaggedSegment> FlaggedSegments,
    IReadOnlyList<SprintMembershipSpan> SprintMemberships,
    int ReopenCount,
    int ReassignmentCount,
    int BackflowCount);

/// <summary>
/// Reconstructs an issue's flow by replaying its changelog: time in each status attributed to
/// the assignee at the time (the intersection of the status and assignee timelines), blocked
/// spans, sprint membership intervals, and churn counts. Pure and deterministic — all
/// wall-clock, all UTC — so it is unit-tested directly without a database.
/// </summary>
public static class IssueFlowReplay
{
    private sealed record Interval<T>(T Value, DateTimeOffset Start, DateTimeOffset End, bool OpenEnd);

    /// <param name="statusRank">
    /// Maps a status name to its category rank (To Do = 0, In Progress = 1, Done = 2), or null
    /// when unknown. Used only for backflow detection.
    /// </param>
    public static IssueFlow Compute(IssueFlowInput input, Func<string, int?> statusRank)
    {
        var entries = input.Entries.OrderBy(e => e.Created).ToList();
        var resolved = input.ResolvedAt is not null;
        var horizonEnd = Max(input.ResolvedAt ?? input.Now, input.CreatedAt);

        var statusIntervals = BuildStatusIntervals(entries, input, horizonEnd, resolved, out var statusOrder);
        var assigneeIntervals = BuildAssigneeIntervals(entries, input, horizonEnd, resolved);
        var flaggedIntervals = BuildFlaggedIntervals(entries, input.CreatedAt, horizonEnd, resolved);

        var statusSegments = new List<StatusSegment>();
        foreach (var s in statusIntervals)
        {
            foreach (var a in Overlaps(s, assigneeIntervals, resolved, horizonEnd))
            {
                statusSegments.Add(new StatusSegment(s.Value, a.Assignee, a.Start, a.End));
            }
        }

        var flaggedSegments = new List<FlaggedSegment>();
        foreach (var f in flaggedIntervals)
        {
            foreach (var a in Overlaps(f, assigneeIntervals, resolved, horizonEnd))
            {
                flaggedSegments.Add(new FlaggedSegment(a.Assignee, a.Start, a.End));
            }
        }

        var reassignments = entries.Sum(e => e.Items.Count(i => IsField(i, "assignee")));
        var reopens = entries.Sum(e => e.Items.Count(i => IsField(i, "resolution") && string.IsNullOrEmpty(i.To)));
        var backflow = CountBackflow(statusOrder, statusRank);

        return new IssueFlow(
            statusSegments,
            flaggedSegments,
            BuildSprintMemberships(entries, input.CreatedAt),
            reopens,
            reassignments,
            backflow);
    }

    // --- status timeline ---
    private static List<Interval<string>> BuildStatusIntervals(
        List<ChangelogEntry> entries, IssueFlowInput input, DateTimeOffset horizonEnd, bool resolved,
        out List<string> order)
    {
        // The initial status is the first status change's "from"; absent any change, the issue
        // stayed in its current status the whole time.
        string? initial = null;
        var changes = new List<(DateTimeOffset At, string Value)>();
        foreach (var entry in entries)
        {
            foreach (var item in entry.Items.Where(i => IsField(i, "status")))
            {
                initial ??= item.FromString;
                if (!string.IsNullOrEmpty(item.ToStr))
                {
                    changes.Add((entry.Created, item.ToStr!));
                }
            }
        }

        order = new List<string>();
        var startStatus = initial ?? input.CurrentStatus;
        if (startStatus is not null)
        {
            order.Add(startStatus);
        }

        order.AddRange(changes.Select(c => c.Value));

        return startStatus is null
            ? []
            : BuildTimeline(startStatus, changes, input.CreatedAt, horizonEnd, resolved)
                .Where(i => !string.IsNullOrEmpty(i.Value))
                .ToList();
    }

    // --- assignee timeline (null value = unassigned) ---
    private static List<Interval<string?>> BuildAssigneeIntervals(
        List<ChangelogEntry> entries, IssueFlowInput input, DateTimeOffset horizonEnd, bool resolved)
    {
        var items = entries
            .SelectMany(e => e.Items.Where(i => IsField(i, "assignee")).Select(i => (e.Created, i)))
            .ToList();

        // Original assignee is the first change's "from"; absent any change, the current one held.
        var initial = items.Count > 0 ? Blank(items[0].i.From) : Blank(input.CurrentAssigneeAccountId);
        var changes = items.Select(x => (x.Created, Blank(x.i.To))).ToList();

        return BuildTimeline(initial, changes, input.CreatedAt, horizonEnd, resolved);
    }

    // --- flagged (blocked) timeline: on when the flag's toString is non-empty ---
    private static List<Interval<bool>> BuildFlaggedIntervals(
        List<ChangelogEntry> entries, DateTimeOffset createdAt, DateTimeOffset horizonEnd, bool resolved)
    {
        bool? initial = null;
        var changes = new List<(DateTimeOffset At, bool Value)>();
        foreach (var entry in entries)
        {
            foreach (var item in entry.Items.Where(IsFlagged))
            {
                initial ??= !string.IsNullOrEmpty(item.FromString);
                changes.Add((entry.Created, !string.IsNullOrEmpty(item.ToStr)));
            }
        }

        if (changes.Count == 0)
        {
            return [];
        }

        return BuildTimeline(initial ?? false, changes, createdAt, horizonEnd, resolved)
            .Where(i => i.Value) // keep only the "flagged on" spans
            .ToList();
    }

    // --- sprint membership intervals from the Sprint field's id lists ---
    private static List<SprintMembershipSpan> BuildSprintMemberships(
        List<ChangelogEntry> entries, DateTimeOffset createdAt)
    {
        var sprintItems = entries
            .SelectMany(e => e.Items.Where(i => IsField(i, "sprint")).Select(i => (e.Created, i)))
            .OrderBy(x => x.Created)
            .ToList();
        if (sprintItems.Count == 0)
        {
            return [];
        }

        var names = new Dictionary<long, string>();
        foreach (var (_, item) in sprintItems)
        {
            MergeNames(names, item.From, item.FromString);
            MergeNames(names, item.To, item.ToStr);
        }

        // Membership before the first recorded change: the first item's "from" set, present
        // from issue creation.
        var open = new Dictionary<long, DateTimeOffset>();
        foreach (var id in ParseIds(sprintItems[0].i.From))
        {
            open[id] = createdAt;
        }

        var spans = new List<SprintMembershipSpan>();
        foreach (var (at, item) in sprintItems)
        {
            var next = ParseIds(item.To).ToHashSet();
            foreach (var id in ParseIds(item.From).Where(id => !next.Contains(id)))
            {
                if (open.Remove(id, out var addedAt))
                {
                    spans.Add(new SprintMembershipSpan(id, Name(names, id), addedAt, at));
                }
            }

            foreach (var id in next.Where(id => !open.ContainsKey(id)))
            {
                open[id] = at;
            }
        }

        // Sprints the issue never left stay open (RemovedAt null = still a member).
        foreach (var (id, addedAt) in open)
        {
            spans.Add(new SprintMembershipSpan(id, Name(names, id), addedAt, null));
        }

        return spans;
    }

    // Builds a step-function timeline: the value held between successive change points, from
    // the issue's creation to the horizon (open-ended when the issue is unresolved).
    private static List<Interval<T>> BuildTimeline<T>(
        T initial, List<(DateTimeOffset At, T Value)> changes, DateTimeOffset start, DateTimeOffset horizonEnd, bool resolved)
    {
        var intervals = new List<Interval<T>>();
        var cur = initial;
        var curStart = start;
        foreach (var (at, val) in changes.OrderBy(c => c.At))
        {
            var end = Max(at, curStart);
            if (end > curStart)
            {
                intervals.Add(new Interval<T>(cur, curStart, end, false));
            }

            cur = val;
            curStart = end;
        }

        var finalEnd = Max(horizonEnd, curStart);
        if (finalEnd > curStart)
        {
            intervals.Add(new Interval<T>(cur, curStart, finalEnd, !resolved));
        }

        return intervals;
    }

    // Overlaps a value-interval with the assignee timeline, yielding the assignee-attributed
    // sub-spans (EndedAt null when the sub-span reaches an unresolved horizon).
    private static IEnumerable<(string? Assignee, DateTimeOffset Start, DateTimeOffset? End)> Overlaps<T>(
        Interval<T> span, List<Interval<string?>> assignees, bool resolved, DateTimeOffset horizonEnd)
    {
        var result = new List<(string?, DateTimeOffset, DateTimeOffset?)>();
        foreach (var a in assignees)
        {
            var start = Max(span.Start, a.Start);
            var end = Min(span.End, a.End);
            if (end <= start)
            {
                continue;
            }

            var open = !resolved && end == horizonEnd;
            result.Add((a.Value, start, open ? null : end));
        }

        return result;
    }

    private static int CountBackflow(List<string> statusOrder, Func<string, int?> statusRank)
    {
        var count = 0;
        for (var i = 1; i < statusOrder.Count; i++)
        {
            var prev = statusRank(statusOrder[i - 1]);
            var next = statusRank(statusOrder[i]);
            if (prev is not null && next is not null && next < prev)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsField(ChangelogItem item, string field) =>
        string.Equals(item.Field, field, StringComparison.OrdinalIgnoreCase);

    // The flag field is reported as "Flagged" (or fieldId "flagged") across instances.
    private static bool IsFlagged(ChangelogItem item) =>
        string.Equals(item.Field, "Flagged", StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.FieldId, "flagged", StringComparison.OrdinalIgnoreCase);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IEnumerable<long> ParseIds(string? list) =>
        string.IsNullOrWhiteSpace(list)
            ? []
            : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (long?)null)
                .Where(id => id is not null)
                .Select(id => id!.Value);

    // Sprint id lists and their name lists are parallel; pair them positionally, best-effort.
    private static void MergeNames(Dictionary<long, string> into, string? ids, string? names)
    {
        if (string.IsNullOrWhiteSpace(ids) || string.IsNullOrWhiteSpace(names))
        {
            return;
        }

        var idParts = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nameParts = names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < idParts.Length && i < nameParts.Length; i++)
        {
            if (long.TryParse(idParts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                into[id] = nameParts[i];
            }
        }
    }

    private static string? Name(Dictionary<long, string> names, long id) =>
        names.TryGetValue(id, out var name) ? name : null;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
