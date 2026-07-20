using Factarium.Application.Transform;

namespace Factarium.Tests.Application;

public class IssueFlowReplayTests
{
    private static DateTimeOffset D(int day) => new(2026, 7, day, 0, 0, 0, TimeSpan.Zero);

    private static int? Rank(string status) => status switch
    {
        "To Do" => 0,
        "In Progress" => 1,
        "Done" => 2,
        _ => null,
    };

    private static ChangelogEntry Entry(DateTimeOffset at, params ChangelogItem[] items) =>
        new(at, "author-acc", "Author", items);

    private static ChangelogItem Status(string from, string to) => new("status", null, null, from, null, to);
    private static ChangelogItem Assignee(string? from, string? to) => new("assignee", null, from, from, to, to);
    private static ChangelogItem Sprint(string from, string to) => new("Sprint", null, from, from, to, to);
    private static ChangelogItem FlagOn() => new("Flagged", "flagged", null, "", null, "Impediment");
    private static ChangelogItem FlagOff() => new("Flagged", "flagged", null, "Impediment", null, "");
    private static ChangelogItem Resolution(string to) => new("resolution", "resolution", null, null, to, to);

    [Fact]
    public void Splits_time_in_status_for_a_single_assignee_on_a_resolved_issue()
    {
        var input = new IssueFlowInput(
            D(1), D(4), "Done", "acc-1",
            [Entry(D(2), Status("To Do", "In Progress")), Entry(D(3), Status("In Progress", "Done"))],
            D(10));

        var flow = IssueFlowReplay.Compute(input, Rank);
        var segments = flow.StatusSegments.OrderBy(s => s.StartedAt).ToList();

        Assert.Collection(
            segments,
            s => Assert.Equal(("To Do", "acc-1", D(1), (DateTimeOffset?)D(2)), (s.Status, s.AssigneeAccountId, s.StartedAt, s.EndedAt)),
            s => Assert.Equal(("In Progress", "acc-1", D(2), (DateTimeOffset?)D(3)), (s.Status, s.AssigneeAccountId, s.StartedAt, s.EndedAt)),
            s => Assert.Equal(("Done", "acc-1", D(3), (DateTimeOffset?)D(4)), (s.Status, s.AssigneeAccountId, s.StartedAt, s.EndedAt)));
    }

    [Fact]
    public void Attributes_status_time_to_the_assignee_at_the_time_and_leaves_open_issues_running()
    {
        // Original assignee acc-1 (first change's "from"); reassigned to acc-2 on day 3; unresolved.
        var input = new IssueFlowInput(
            D(1), null, "In Progress", "acc-2",
            [Entry(D(2), Status("To Do", "In Progress")), Entry(D(3), Assignee("acc-1", "acc-2"))],
            D(5));

        var flow = IssueFlowReplay.Compute(input, Rank);

        var inProgress = flow.StatusSegments.Where(s => s.Status == "In Progress").OrderBy(s => s.StartedAt).ToList();
        Assert.Collection(
            inProgress,
            s => Assert.Equal(("acc-1", D(2), (DateTimeOffset?)D(3)), (s.AssigneeAccountId, s.StartedAt, s.EndedAt)),
            s => Assert.Equal(("acc-2", D(3), (DateTimeOffset?)null), (s.AssigneeAccountId, s.StartedAt, s.EndedAt))); // open

        Assert.Equal("acc-1", flow.StatusSegments.Single(s => s.Status == "To Do").AssigneeAccountId);
        Assert.Equal(1, flow.ReassignmentCount);
    }

    [Fact]
    public void Captures_a_flagged_span_attributed_to_the_assignee()
    {
        var input = new IssueFlowInput(
            D(1), D(5), "Done", "acc-1",
            [Entry(D(2), FlagOn()), Entry(D(4), FlagOff())],
            D(10));

        var flow = IssueFlowReplay.Compute(input, Rank);

        var flagged = Assert.Single(flow.FlaggedSegments);
        Assert.Equal((D(2), (DateTimeOffset?)D(4), "acc-1"), (flagged.StartedAt, flagged.EndedAt, flagged.AssigneeAccountId));
    }

    [Fact]
    public void Reconstructs_sprint_membership_intervals()
    {
        var input = new IssueFlowInput(
            D(1), D(5), "Done", "acc-1",
            [Entry(D(2), Sprint("", "5")), Entry(D(3), Sprint("5", "6"))],
            D(10));

        var flow = IssueFlowReplay.Compute(input, Rank);
        var byId = flow.SprintMemberships.ToDictionary(m => m.SprintId);

        Assert.Equal((D(2), (DateTimeOffset?)D(3)), (byId[5].AddedAt, byId[5].RemovedAt));
        Assert.Equal((D(3), (DateTimeOffset?)null), (byId[6].AddedAt, byId[6].RemovedAt)); // still a member
    }

    [Fact]
    public void Counts_backflow_and_reopens()
    {
        var input = new IssueFlowInput(
            D(1), D(4), "In Progress", "acc-1",
            [
                Entry(D(2), Status("To Do", "In Progress")),
                Entry(D(3), Status("In Progress", "Done"), Resolution("Done")),
                Entry(D(4), Status("Done", "In Progress"), Resolution("")), // backward + resolution cleared
            ],
            D(10));

        var flow = IssueFlowReplay.Compute(input, Rank);

        Assert.Equal(1, flow.BackflowCount);
        Assert.Equal(1, flow.ReopenCount);
    }

    [Fact]
    public void Treats_the_whole_life_as_current_status_when_there_are_no_status_changes()
    {
        var input = new IssueFlowInput(D(1), null, "To Do", "acc-1", [], D(3));

        var flow = IssueFlowReplay.Compute(input, Rank);

        var segment = Assert.Single(flow.StatusSegments);
        Assert.Equal(("To Do", "acc-1", D(1), (DateTimeOffset?)null), (segment.Status, segment.AssigneeAccountId, segment.StartedAt, segment.EndedAt));
    }
}
