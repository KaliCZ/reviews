using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Reviews.Infrastructure.Entities;
using Reviews.Shared;
using StrongTypes;

namespace Reviews.Api.Tests.Integration;

// Raw-JSON assertions — typed-DTO round-trips would silently absorb
// wire-shape changes (e.g. createdAtUtc → createdAt) that the SPA cares about.
[Collection(IntegrationTestCollection.Name)]
public class ApiIntegrationTests(IntegrationTestFixture fx)
{
    private static readonly Regex SubmitWorkflowIdPattern = new(
        @"^submit-review-[0-9a-fA-F]{32}$", RegexOptions.Compiled);
    private static readonly Regex EditWorkflowIdPattern = new(
        @"^edit-review-[0-9a-fA-F]{32}-[0-9a-fA-F]{32}$", RegexOptions.Compiled);
    private static readonly Regex VoteWorkflowIdPattern = new(
        @"^vote-[0-9a-fA-F]{32}-[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    // -- /api/products (list) --------------------------------------------

    [Fact]
    public async Task Get_products_returns_seeded_catalog()
    {
        var response = await fx.ApiClient.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.True(root.GetArrayLength() >= 10, $"expected at least 10 seeded products, got {root.GetArrayLength()}");

        foreach (var item in root.EnumerateArray())
        {
            AssertString(item, "slug");
            AssertString(item, "name");
            AssertNumber(item, "averageRating");
            AssertNumber(item, "reviewCount");
        }
    }

    // -- /api/products/{slug} (detail) -----------------------------------

    [Fact]
    public async Task Get_product_detail_returns_full_shape()
    {
        var response = await fx.ApiClient.GetAsync("/api/products/sony-wh-1000xm5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("sony-wh-1000xm5", root.GetProperty("slug").GetString());
        AssertString(root, "name");
        AssertString(root, "description");
        Assert.True(root.GetProperty("averageRating").GetDouble() > 0);
        Assert.True(root.GetProperty("reviewCount").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("myReviewId").ValueKind);
    }

    // -- /api/config -----------------------------------------------------

    [Fact]
    public async Task Get_config_returns_turnstile_site_key()
    {
        var response = await fx.ApiClient.GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = doc.RootElement.GetProperty("turnstileSiteKey").GetString();
        Assert.False(string.IsNullOrEmpty(key));
    }

    // -- /api/products/{slug}/reviews (listing) --------------------------

    [Fact]
    public async Task Get_product_reviews_returns_paged_items()
    {
        var response = await fx.ApiClient.GetAsync("/api/products/sony-wh-1000xm5/reviews");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.True(root.GetProperty("pageSize").GetInt32() > 0);
        Assert.True(root.GetProperty("totalCount").GetInt32() >= 5); // seed has 5 for this product
        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(items.GetArrayLength() > 0);

        foreach (var item in items.EnumerateArray())
        {
            AssertString(item, "id");
            AssertNumber(item, "productId");
            AssertString(item, "title");
            AssertString(item, "body");
            AssertNumber(item, "rating");
            AssertString(item, "createdAtUtc");
            AssertString(item, "updatedAtUtc");
            Assert.Contains(item.GetProperty("myVote").ValueKind, new[] { JsonValueKind.Null, JsonValueKind.True, JsonValueKind.False });
            Assert.Equal(JsonValueKind.False, item.GetProperty("mine").ValueKind);
        }
    }

    // -- POST /api/reviews (auto-approve) --------------------------------

    [Fact]
    public async Task Submit_4_star_review_persists_as_approved()
    {
        // Product 7 is fixture-unique to avoid colliding with sibling tests.
        const long productId = 7;
        var (body, expectedTitle, expectedBody) = MakeSubmitPayload(productId, rating: 4);
        var response = await fx.ApiClient.PostAsync("/api/reviews", JsonContent(body));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var workflowId = doc.RootElement.GetProperty("workflowId").GetString()!;
        Assert.Matches(SubmitWorkflowIdPattern, workflowId);
        Assert.Equal("submitted", doc.RootElement.GetProperty("status").GetString());

        await fx.TemporalClient.GetWorkflowHandle(workflowId).GetResultAsync<string>();

        await using var db = fx.CreateDbContext();
        var review = await db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Title == expectedTitle.ToNonEmpty())
            .SingleAsync();
        Assert.Equal(ReviewStatus.Approved, review.Status);
        Assert.Equal(expectedBody, review.Body.Value);
        Assert.Equal(Rating.Four, review.Rating);
    }

    // -- POST /api/reviews (5-star, moderation gate) ---------------------

    [Fact]
    public async Task Submit_5_star_review_waits_for_approve_signal()
    {
        const long productId = 5;
        var (body, expectedTitle, _) = MakeSubmitPayload(productId, rating: 5);
        var response = await fx.ApiClient.PostAsync("/api/reviews", JsonContent(body));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var workflowId = doc.RootElement.GetProperty("workflowId").GetString()!;

        // Persist activity runs before WaitConditionAsync so the Pending row lands first.
        var pending = await fx.WaitForAsync(async () =>
        {
            await using var db = fx.CreateDbContext();
            return await db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId == productId && r.Title == expectedTitle.ToNonEmpty())
                .SingleOrDefaultAsync();
        }, what: "pending review row");
        Assert.Equal(ReviewStatus.Pending, pending.Status);

        var handle = fx.TemporalClient.GetWorkflowHandle(workflowId);
        await handle.SignalAsync(SubmitReviewWorkflow.ApproveSignal, new object?[] { (string?)null });

        var result = await handle.GetResultAsync<string>();
        Assert.Equal("approved", result);

        await using var db = fx.CreateDbContext();
        var approved = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == pending.Id)
            .SingleAsync();
        Assert.Equal(ReviewStatus.Approved, approved.Status);
    }

    // -- POST /api/reviews/{id}/vote --------------------------------------

    [Fact]
    public async Task Vote_creates_row_then_flip_updates_score()
    {
        // RecordVote requires Approved, so submit a 4-star (auto-approved) first.
        const long productId = 9;
        var (body, expectedTitle, _) = MakeSubmitPayload(productId, rating: 4);
        var submitResponse = await fx.ApiClient.PostAsync("/api/reviews", JsonContent(body));
        Assert.Equal(HttpStatusCode.Accepted, submitResponse.StatusCode);
        var submitWorkflowId = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("workflowId").GetString()!;
        await fx.TemporalClient.GetWorkflowHandle(submitWorkflowId).GetResultAsync<string>();

        Guid reviewId;
        await using (var db = fx.CreateDbContext())
        {
            reviewId = await db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId == productId && r.Title == expectedTitle.ToNonEmpty())
                .Select(r => r.Id)
                .SingleAsync();
        }

        // Upvote.
        var up = await fx.ApiClient.PostAsync($"/api/reviews/{reviewId}/vote",
            JsonContent("""{"isUpvote": true}"""));
        Assert.Equal(HttpStatusCode.Accepted, up.StatusCode);
        using (var d = JsonDocument.Parse(await up.Content.ReadAsStringAsync()))
        {
            Assert.Matches(VoteWorkflowIdPattern, d.RootElement.GetProperty("workflowId").GetString()!);
            Assert.Equal("voted", d.RootElement.GetProperty("status").GetString());
            await fx.TemporalClient.GetWorkflowHandle(
                d.RootElement.GetProperty("workflowId").GetString()!).GetResultAsync<string>();
        }

        await using (var db = fx.CreateDbContext())
        {
            var vote = await db.ReviewVotes.AsNoTracking().SingleAsync(v => v.ReviewId == reviewId);
            Assert.True(vote.IsUpvote);
            var review = await db.Reviews.AsNoTracking().SingleAsync(r => r.Id == reviewId);
            Assert.Equal(1, review.Score);
        }

        // Vote workflow id is deterministic per (review, voter); the second
        // call may join an already-done workflow (UseExisting policy).
        var down = await fx.ApiClient.PostAsync($"/api/reviews/{reviewId}/vote",
            JsonContent("""{"isUpvote": false}"""));
        Assert.Equal(HttpStatusCode.Accepted, down.StatusCode);
        var downWorkflowId = JsonDocument.Parse(await down.Content.ReadAsStringAsync())
            .RootElement.GetProperty("workflowId").GetString()!;
        try { await fx.TemporalClient.GetWorkflowHandle(downWorkflowId).GetResultAsync<string>(); }
        catch (Temporalio.Exceptions.WorkflowAlreadyStartedException) { /* benign */ }

        // The second start may join the existing handle without re-running,
        // so observe DB state rather than the workflow result.
        await fx.WaitForAsync<object>(async () =>
        {
            await using var db = fx.CreateDbContext();
            var v = await db.ReviewVotes.AsNoTracking().SingleAsync(v => v.ReviewId == reviewId);
            return v.IsUpvote ? null : new object();
        }, what: "downvote flip");

        await using (var db = fx.CreateDbContext())
        {
            var review = await db.Reviews.AsNoTracking().SingleAsync(r => r.Id == reviewId);
            Assert.Equal(-1, review.Score);
        }
    }

    // -- PUT /api/reviews/{id} -------------------------------------------

    [Fact]
    public async Task Edit_within_one_hour_applies_immediately()
    {
        // Fresh review stays under the 1h cutoff so the edit auto-applies.
        const long productId = 8;
        var (body, originalTitle, _) = MakeSubmitPayload(productId, rating: 3);
        var submit = await fx.ApiClient.PostAsync("/api/reviews", JsonContent(body));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        await fx.TemporalClient.GetWorkflowHandle(
            JsonDocument.Parse(await submit.Content.ReadAsStringAsync())
                .RootElement.GetProperty("workflowId").GetString()!).GetResultAsync<string>();

        Guid reviewId;
        DateTime originalCreatedAt;
        await using (var db = fx.CreateDbContext())
        {
            var row = await db.Reviews.AsNoTracking()
                .Where(r => r.ProductId == productId && r.Title == originalTitle.ToNonEmpty())
                .Select(r => new { r.Id, r.CreatedAtUtc })
                .SingleAsync();
            reviewId = row.Id;
            originalCreatedAt = row.CreatedAtUtc;
        }

        var newTitle = $"Edited title {Guid.NewGuid():N}";
        var newBody = $"Edited body {Guid.NewGuid():N}";
        var editPayload = $$"""
            {
              "rating": 2,
              "title": {{JsonSerializer.Serialize(newTitle)}},
              "body": {{JsonSerializer.Serialize(newBody)}}
            }
            """;
        var edit = await fx.ApiClient.PutAsync($"/api/reviews/{reviewId}", JsonContent(editPayload));
        Assert.Equal(HttpStatusCode.Accepted, edit.StatusCode);
        using var editDoc = JsonDocument.Parse(await edit.Content.ReadAsStringAsync());
        var editWorkflowId = editDoc.RootElement.GetProperty("workflowId").GetString()!;
        Assert.Matches(EditWorkflowIdPattern, editWorkflowId);
        Assert.Equal("edit-submitted", editDoc.RootElement.GetProperty("status").GetString());

        var editResult = await fx.TemporalClient.GetWorkflowHandle(editWorkflowId).GetResultAsync<string>();
        Assert.Equal("applied", editResult);

        await using var dbCheck = fx.CreateDbContext();
        var edited = await dbCheck.Reviews.AsNoTracking().SingleAsync(r => r.Id == reviewId);
        Assert.Equal(newTitle, edited.Title.Value);
        Assert.Equal(newBody, edited.Body.Value);
        Assert.Equal(Rating.Two, edited.Rating);
        Assert.True(edited.UpdatedAtUtc >= originalCreatedAt,
            $"UpdatedAtUtc ({edited.UpdatedAtUtc:O}) should be at or after CreatedAtUtc ({originalCreatedAt:O})");
    }

    // -- Denormalized Product.ReviewCount / AverageRating (issue #5) ----

    [Fact]
    public async Task Submit_then_approve_recomputes_product_aggregates()
    {
        // Product 6 (iPad Air) has 4 seeded reviews with ratings {5, 4, 5, 4}.
        const long productId = 6;

        long beforeCount;
        double beforeAvg;
        await using (var db = fx.CreateDbContext())
        {
            var p = await db.Products.AsNoTracking()
                .Where(x => x.Id == productId)
                .Select(x => new { x.ReviewCount, x.AverageRating })
                .SingleAsync();
            beforeCount = p.ReviewCount;
            beforeAvg = p.AverageRating;
        }
        Assert.Equal(4, beforeCount);
        Assert.True(beforeAvg > 0);

        // 4-star auto-approves (no moderator signal needed).
        var (body, _, _) = MakeSubmitPayload(productId, rating: 4);
        var submit = await fx.ApiClient.PostAsync("/api/reviews", JsonContent(body));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var workflowId = JsonDocument.Parse(await submit.Content.ReadAsStringAsync())
            .RootElement.GetProperty("workflowId").GetString()!;
        await fx.TemporalClient.GetWorkflowHandle(workflowId).GetResultAsync<string>();

        await using var dbCheck = fx.CreateDbContext();
        var after = await dbCheck.Products.AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new { x.ReviewCount, x.AverageRating })
            .SingleAsync();
        Assert.Equal(beforeCount + 1, after.ReviewCount);

        // Truth check via SQL aggregation against the source-of-truth rows.
        var truthAvg = await dbCheck.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .AverageAsync(r => (double)(short)r.Rating);
        Assert.Equal(truthAvg, after.AverageRating, precision: 6);
    }

    // -- POST /api/reviews — negative validation case --------------------

    [Fact]
    public async Task Empty_post_returns_400_with_validation_problem_details()
    {
        var response = await fx.ApiClient.PostAsync("/api/reviews",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        AssertString(root, "title");
        AssertString(root, "type");
        Assert.Equal(400, root.GetProperty("status").GetInt32());

        var errors = root.GetProperty("errors");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);

        // MVC model validation flags every missing non-nullable reference field.
        var keys = errors.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("title", keys);
        Assert.Contains("body", keys);
        Assert.Contains("turnstileToken", keys);
    }

    // ---- helpers ----

    private static StringContent JsonContent(string raw) =>
        new StringContent(raw, Encoding.UTF8, "application/json");

    private static void AssertString(JsonElement parent, string property)
    {
        var prop = parent.GetProperty(property);
        Assert.Equal(JsonValueKind.String, prop.ValueKind);
        Assert.False(string.IsNullOrEmpty(prop.GetString()), $"expected non-empty string at .{property}");
    }

    private static void AssertNumber(JsonElement parent, string property)
    {
        var prop = parent.GetProperty(property);
        Assert.Equal(JsonValueKind.Number, prop.ValueKind);
    }

    // Title carries a Guid so DB lookups find this exact row across sibling tests.
    private static (string Body, string Title, string Body2) MakeSubmitPayload(long productId, int rating)
    {
        var title = $"Test review {Guid.NewGuid():N}";
        var body = $"Integration test body {Guid.NewGuid():N}";
        var json = $$"""
            {
              "productId": {{productId}},
              "rating": {{rating}},
              "title": {{JsonSerializer.Serialize(title)}},
              "body": {{JsonSerializer.Serialize(body)}},
              "turnstileToken": "stub-token"
            }
            """;
        return (json, title, body);
    }
}
