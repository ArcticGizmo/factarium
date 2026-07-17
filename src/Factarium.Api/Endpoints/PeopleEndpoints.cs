using Factarium.Application.Aggregate;
using Factarium.Domain.People;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

public sealed record CreatePersonRequest(string DisplayName);
public sealed record LinkIdentityRequest(Guid PersonId);

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder app)
    {
        var people = app.MapGroup("/api/people");
        var identities = app.MapGroup("/api/identities");

        people.MapGet("", async (FactariumDbContext db, CancellationToken ct) =>
        {
            var persons = await db.People.OrderBy(p => p.DisplayName).ToListAsync(ct);
            var identityRows = await db.SourceIdentities.ToListAsync(ct);

            return Results.Ok(persons.Select(p => new
            {
                p.Id,
                p.DisplayName,
                Identities = identityRows
                    .Where(i => i.PersonId == p.Id)
                    .Select(i => new { i.Id, i.Source, i.Login })
                    .ToList(),
            }));
        });

        people.MapPost("", async (CreatePersonRequest request, FactariumDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.BadRequest("DisplayName is required.");
            }

            var person = new Person
            {
                Id = Guid.NewGuid(),
                DisplayName = request.DisplayName.Trim(),
                CreatedAt = clock.GetUtcNow(),
            };
            db.People.Add(person);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/people/{person.Id}", new { person.Id, person.DisplayName });
        });

        identities.MapGet("", async (FactariumDbContext db, CancellationToken ct) =>
        {
            var rows = await db.SourceIdentities.OrderBy(i => i.Login).ToListAsync(ct);
            var names = await db.People.ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

            return Results.Ok(rows.Select(i => new
            {
                i.Id,
                i.Source,
                i.Login,
                i.PersonId,
                PersonName = i.PersonId is { } pid && names.TryGetValue(pid, out var n) ? n : null,
            }));
        });

        // Link an identity to a person, then re-aggregate so metrics re-attribute immediately.
        identities.MapPost("{id:guid}/link", async (
            Guid id, LinkIdentityRequest request, FactariumDbContext db, IAggregateService aggregate, CancellationToken ct) =>
        {
            var identity = await db.SourceIdentities.FindAsync([id], ct);
            if (identity is null)
            {
                return Results.NotFound();
            }

            if (!await db.People.AnyAsync(p => p.Id == request.PersonId, ct))
            {
                return Results.BadRequest("Person not found.");
            }

            identity.PersonId = request.PersonId;
            await db.SaveChangesAsync(ct);
            await aggregate.AggregateAsync(ct);
            return Results.NoContent();
        });

        identities.MapPost("{id:guid}/unlink", async (
            Guid id, FactariumDbContext db, IAggregateService aggregate, CancellationToken ct) =>
        {
            var identity = await db.SourceIdentities.FindAsync([id], ct);
            if (identity is null)
            {
                return Results.NotFound();
            }

            identity.PersonId = null;
            await db.SaveChangesAsync(ct);
            await aggregate.AggregateAsync(ct);
            return Results.NoContent();
        });

        return app;
    }
}
