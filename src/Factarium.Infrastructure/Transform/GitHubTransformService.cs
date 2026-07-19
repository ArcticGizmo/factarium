using System.Text.Json;
using Factarium.Application.Transform;
using Factarium.Domain.Canonical;
using Factarium.Domain.People;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Transform;

/// <summary>
/// Parses stored GitHub JSON into canonical entities, attributing each to a
/// SourceIdentity (created on demand). Commits are stored flat (<c>repo</c>,
/// <c>author_login</c>, …) and attributed to their author; pull requests and
/// reviews still carry <c>repository_full_name</c> / <c>pull_request_number</c>
/// provenance fields.
/// </summary>
internal sealed class GitHubTransformService(FactariumDbContext db, TimeProvider clock) : ITransformService
{
    private const string Source = "github";

    public async Task<TransformResult> TransformAsync(CancellationToken cancellationToken)
    {
        var raws = await db.RawRecords.Where(r => r.Source == Source).ToListAsync(cancellationToken);
        var byType = raws.GroupBy(r => r.EntityType).ToDictionary(g => g.Key, g => g.ToList());

        var identities = await EnsureIdentitiesAsync(byType, cancellationToken);

        var repositories = await UpsertRepositoriesAsync(Records(byType, "repository"), cancellationToken);
        var commits = await UpsertCommitsAsync(Records(byType, "commit"), identities, cancellationToken);
        var pullRequests = await UpsertPullRequestsAsync(Records(byType, "pull_request"), identities, cancellationToken);
        var reviews = await UpsertReviewsAsync(Records(byType, "review"), identities, cancellationToken);

        return new TransformResult(repositories, commits, pullRequests, reviews, identities.Count);
    }

    private async Task<Dictionary<string, Guid>> EnsureIdentitiesAsync(
        Dictionary<string, List<RawRecord>> byType, CancellationToken cancellationToken)
    {
        var existing = await db.SourceIdentities
            .Where(i => i.Source == Source)
            .ToListAsync(cancellationToken);

        var map = existing.ToDictionary(i => i.Login, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var discovered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        void Note(JsonElement payload, string userProperty)
        {
            if (payload.TryGetProperty(userProperty, out var user) && user.ValueKind == JsonValueKind.Object)
            {
                var login = Str(user, "login");
                if (login is not null)
                {
                    discovered[login] = user.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                        ? id.GetInt64().ToString()
                        : null;
                }
            }
        }

        // Commits carry the author as flat scalars (author_login/author_id). The
        // committer_* fallback covers records synced before the author switch.
        foreach (var record in Records(byType, "commit"))
        {
            var payload = Root(record);
            var login = Str(payload, "author_login") ?? Str(payload, "committer_login");
            if (login is not null)
            {
                discovered[login] = ExternalId(payload, "author_id") ?? ExternalId(payload, "committer_id");
            }
        }

        foreach (var record in Records(byType, "pull_request").Concat(Records(byType, "review")))
        {
            Note(Root(record), "user");
        }

        var now = clock.GetUtcNow();
        foreach (var (login, externalId) in discovered)
        {
            if (map.ContainsKey(login))
            {
                continue;
            }

            var identity = new SourceIdentity
            {
                Id = Guid.NewGuid(),
                Source = Source,
                Login = login,
                ExternalId = externalId,
                DisplayName = login,
                PersonId = null,
                FirstSeenAt = now,
            };
            db.SourceIdentities.Add(identity);
            map[login] = identity.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return map;
    }

    private async Task<int> UpsertRepositoriesAsync(List<RawRecord> records, CancellationToken cancellationToken)
    {
        var existing = await db.CanonicalRepositories
            .Where(r => r.Source == Source)
            .ToDictionaryAsync(r => r.ExternalId, cancellationToken);

        foreach (var record in records)
        {
            var payload = Root(record);
            var externalId = record.SourceId;
            var fullName = Str(payload, "full_name") ?? externalId;

            if (!existing.TryGetValue(externalId, out var repo))
            {
                repo = new CanonicalRepository { Source = Source, ExternalId = externalId, FullName = fullName, Name = "", Owner = "" };
                db.CanonicalRepositories.Add(repo);
                existing[externalId] = repo;
            }

            repo.FullName = fullName;
            repo.Name = Str(payload, "name") ?? fullName.Split('/').Last();
            repo.Owner = payload.TryGetProperty("owner", out var owner) ? Str(owner, "login") ?? "" : fullName.Split('/')[0];
            repo.DefaultBranch = Str(payload, "default_branch");
            repo.UpdatedAt = Timestamp(payload, "updated_at");
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private async Task<int> UpsertCommitsAsync(
        List<RawRecord> records, Dictionary<string, Guid> identities, CancellationToken cancellationToken)
    {
        var existing = await db.CanonicalCommits
            .Where(c => c.Source == Source)
            .ToDictionaryAsync(c => c.RepositoryFullName + "|" + c.Sha, cancellationToken);

        foreach (var record in records)
        {
            var payload = Root(record);
            var sha = Str(payload, "sha");
            var repo = Str(payload, "repo");
            if (sha is null || repo is null)
            {
                continue;
            }

            var key = repo + "|" + sha;
            if (!existing.TryGetValue(key, out var commit))
            {
                commit = new CanonicalCommit { Source = Source, Sha = sha, RepositoryFullName = repo };
                db.CanonicalCommits.Add(commit);
                existing[key] = commit;
            }

            var login = Str(payload, "author_login") ?? Str(payload, "committer_login");
            commit.AuthorLogin = login;
            commit.AuthorIdentityId = Resolve(identities, login);
            commit.Message = Str(payload, "message");
            commit.CommittedAt = Timestamp(payload, "committed_at");
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private async Task<int> UpsertPullRequestsAsync(
        List<RawRecord> records, Dictionary<string, Guid> identities, CancellationToken cancellationToken)
    {
        var existing = await db.CanonicalPullRequests
            .Where(p => p.Source == Source)
            .ToDictionaryAsync(p => p.ExternalId, cancellationToken);

        foreach (var record in records)
        {
            var payload = Root(record);
            var externalId = record.SourceId;

            if (!existing.TryGetValue(externalId, out var pr))
            {
                pr = new CanonicalPullRequest
                {
                    Source = Source,
                    ExternalId = externalId,
                    RepositoryFullName = Str(payload, "repository_full_name") ?? "",
                    State = "unknown",
                };
                db.CanonicalPullRequests.Add(pr);
                existing[externalId] = pr;
            }

            var login = payload.TryGetProperty("user", out var user) ? Str(user, "login") : null;
            pr.RepositoryFullName = Str(payload, "repository_full_name") ?? pr.RepositoryFullName;
            pr.Number = payload.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
            pr.Title = Str(payload, "title");
            pr.State = Str(payload, "state") ?? "unknown";
            pr.MergedAt = Timestamp(payload, "merged_at");
            pr.IsMerged = pr.MergedAt is not null;
            pr.BaseRef = payload.TryGetProperty("base", out var baseObj) ? Str(baseObj, "ref") : null;
            pr.CreatedAt = Timestamp(payload, "created_at");
            pr.ClosedAt = Timestamp(payload, "closed_at");
            pr.AuthorLogin = login;
            pr.AuthorIdentityId = Resolve(identities, login);
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private async Task<int> UpsertReviewsAsync(
        List<RawRecord> records, Dictionary<string, Guid> identities, CancellationToken cancellationToken)
    {
        var existing = await db.CanonicalReviews
            .Where(r => r.Source == Source)
            .ToDictionaryAsync(r => r.ExternalId, cancellationToken);

        foreach (var record in records)
        {
            var payload = Root(record);
            var externalId = record.SourceId;

            if (!existing.TryGetValue(externalId, out var review))
            {
                review = new CanonicalReview
                {
                    Source = Source,
                    ExternalId = externalId,
                    RepositoryFullName = Str(payload, "repository_full_name") ?? "",
                };
                db.CanonicalReviews.Add(review);
                existing[externalId] = review;
            }

            var login = payload.TryGetProperty("user", out var user) ? Str(user, "login") : null;
            review.RepositoryFullName = Str(payload, "repository_full_name") ?? review.RepositoryFullName;
            review.PullRequestNumber = payload.TryGetProperty("pull_request_number", out var pn) ? pn.GetInt32() : 0;
            review.State = Str(payload, "state");
            review.SubmittedAt = Timestamp(payload, "submitted_at");
            review.ReviewerLogin = login;
            review.ReviewerIdentityId = Resolve(identities, login);
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private static Guid? Resolve(Dictionary<string, Guid> identities, string? login) =>
        login is not null && identities.TryGetValue(login, out var id) ? id : null;

    private static List<RawRecord> Records(Dictionary<string, List<RawRecord>> byType, string entityType) =>
        byType.TryGetValue(entityType, out var list) ? list : [];

    private static JsonElement Root(RawRecord record)
    {
        using var document = JsonDocument.Parse(record.Payload);
        return document.RootElement.Clone();
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ExternalId(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64().ToString()
            : null;

    private static DateTimeOffset? Timestamp(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetDateTimeOffset()
            : null;
}
