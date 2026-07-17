using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevInbox.Core.GitHub.Models;
using Microsoft.Extensions.Logging;

namespace DevInbox.Core.GitHub;

public sealed class GitHubGraphQlClient(HttpClient http, ILogger<GitHubGraphQlClient> logger)
{
    private const string Endpoint = "https://api.github.com/graphql";

    public async Task<string> GetViewerLoginAsync(string token, CancellationToken cancellationToken)
    {
        using var document = await ExecuteAsync(token, new { query = PollQuery.ViewerQuery }, cancellationToken);
        return document.RootElement.GetProperty("data").GetProperty("viewer").GetProperty("login").GetString()
            ?? throw new GitHubApiException("Resposta do GitHub sem viewer.login.");
    }

    public async Task<PollSnapshot> PollAsync(string token, string login, CancellationToken cancellationToken)
    {
        var payload = new { query = PollQuery.Text, variables = PollQuery.BuildVariables(login) };
        using var document = await ExecuteAsync(token, payload, cancellationToken);
        return ParseSnapshot(document.RootElement.GetProperty("data"));
    }

    private async Task<JsonDocument> ExecuteAsync(string token, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new GitHubAuthException("GitHub retornou 401 — token inválido ou expirado.");

        response.EnsureSuccessStatusCode();

        var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            var messages = string.Join("; ", errors.EnumerateArray()
                .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                .Where(m => m is not null));

            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            {
                document.Dispose();
                throw new GitHubApiException($"Erro GraphQL: {messages}");
            }

            logger.LogWarning("GraphQL retornou erros parciais: {Messages}", messages);
        }

        return document;
    }

    private static PollSnapshot ParseSnapshot(JsonElement data)
    {
        var login = data.GetProperty("viewer").GetProperty("login").GetString()
            ?? throw new GitHubApiException("Resposta do GitHub sem viewer.login.");

        RateLimitInfo? rateLimit = null;
        if (TryGet(data, "rateLimit") is { } rl)
        {
            rateLimit = new RateLimitInfo(
                rl.GetProperty("cost").GetInt32(),
                rl.GetProperty("remaining").GetInt32(),
                rl.GetProperty("resetAt").GetDateTimeOffset());
        }

        var mine = new List<PrSnapshot>();
        foreach (var node in EnumerateNodes(data, "mine"))
        {
            if (ParsePullRequest(node) is { } pr)
                mine.Add(pr);
        }

        var reviewRequested = new List<PrRef>();
        foreach (var node in EnumerateNodes(data, "reviewRequested"))
        {
            if (GetString(node, "id") is not { } id)
                continue;

            reviewRequested.Add(new PrRef(
                id,
                node.GetProperty("repository").GetProperty("nameWithOwner").GetString() ?? "",
                node.GetProperty("number").GetInt32(),
                GetString(node, "title") ?? "",
                GetString(node, "url") ?? "",
                AuthorLogin(node)));
        }

        return new PollSnapshot(login, rateLimit, mine, reviewRequested);
    }

    private static PrSnapshot? ParsePullRequest(JsonElement node)
    {
        // A search pode devolver issues; o fragmento inline vira um objeto vazio nesses casos.
        if (GetString(node, "id") is not { } id)
            return null;

        var comments = new List<CommentInfo>();
        foreach (var comment in EnumerateNodes(node, "comments"))
        {
            if (ParseComment(comment) is { } parsed)
                comments.Add(parsed);
        }

        var reviews = new List<ReviewInfo>();
        foreach (var review in EnumerateNodes(node, "reviews"))
        {
            if (GetString(review, "id") is not { } reviewId)
                continue;

            reviews.Add(new ReviewInfo(
                reviewId,
                GetString(review, "url") ?? "",
                AuthorLogin(review),
                GetString(review, "state") ?? "",
                GetString(review, "bodyText") ?? "",
                TryGet(review, "submittedAt")?.GetDateTimeOffset()));
        }

        var threads = new List<ThreadInfo>();
        foreach (var thread in EnumerateNodes(node, "reviewThreads"))
        {
            if (GetString(thread, "id") is not { } threadId)
                continue;

            var threadComments = new List<CommentInfo>();
            var participated = false;
            foreach (var comment in EnumerateNodes(thread, "comments"))
            {
                participated |= TryGet(comment, "viewerDidAuthor")?.GetBoolean() ?? false;
                if (ParseComment(comment) is { } parsed)
                    threadComments.Add(parsed);
            }

            threads.Add(new ThreadInfo(
                threadId,
                thread.GetProperty("isResolved").GetBoolean(),
                participated,
                threadComments));
        }

        string? headOid = null;
        string? rollupState = null;
        var failingChecks = new List<CheckContextInfo>();
        if (EnumerateNodes(node, "commits").FirstOrDefault() is { ValueKind: JsonValueKind.Object } commitNode
            && TryGet(commitNode, "commit") is { } commit)
        {
            headOid = GetString(commit, "oid");
            if (TryGet(commit, "statusCheckRollup") is { } rollup)
            {
                rollupState = GetString(rollup, "state");
                foreach (var context in EnumerateNodes(rollup, "contexts"))
                {
                    var typeName = GetString(context, "__typename");
                    if (typeName == "CheckRun")
                    {
                        var conclusion = GetString(context, "conclusion");
                        if (conclusion is "FAILURE" or "TIMED_OUT" or "STARTUP_FAILURE")
                            failingChecks.Add(new CheckContextInfo(GetString(context, "name") ?? "", GetString(context, "detailsUrl")));
                    }
                    else if (typeName == "StatusContext")
                    {
                        var state = GetString(context, "state");
                        if (state is "FAILURE" or "ERROR")
                            failingChecks.Add(new CheckContextInfo(GetString(context, "context") ?? "", GetString(context, "targetUrl")));
                    }
                }
            }
        }

        return new PrSnapshot(
            id,
            node.GetProperty("repository").GetProperty("nameWithOwner").GetString() ?? "",
            node.GetProperty("number").GetInt32(),
            GetString(node, "title") ?? "",
            GetString(node, "url") ?? "",
            TryGet(node, "isDraft")?.GetBoolean() ?? false,
            GetString(node, "mergeable") ?? "UNKNOWN",
            headOid,
            rollupState,
            failingChecks,
            comments,
            reviews,
            threads);
    }

    private static CommentInfo? ParseComment(JsonElement comment)
    {
        if (GetString(comment, "id") is not { } id)
            return null;

        return new CommentInfo(
            id,
            GetString(comment, "url") ?? "",
            AuthorLogin(comment),
            GetString(comment, "bodyText") ?? "",
            TryGet(comment, "createdAt")?.GetDateTimeOffset() ?? DateTimeOffset.MinValue);
    }

    private static IEnumerable<JsonElement> EnumerateNodes(JsonElement parent, string connectionName)
    {
        if (TryGet(parent, connectionName) is not { } connection || TryGet(connection, "nodes") is not { } nodes)
            yield break;

        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Object)
                yield return node;
        }
    }

    private static string? AuthorLogin(JsonElement element)
        => TryGet(element, "author") is { } author ? GetString(author, "login") : null;

    private static string? GetString(JsonElement element, string property)
        => TryGet(element, property)?.GetString();

    private static JsonElement? TryGet(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value
            : null;
}
