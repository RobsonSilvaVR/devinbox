using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DevInbox.Core.GitHub;

public sealed record MentionThread(
    string ThreadId,
    string Repo,
    int PrNumber,
    string PrTitle,
    string UpdatedAt,
    string? LatestCommentApiUrl);

public sealed record MentionDetails(string? HtmlUrl, string? Author, string? Body);

public sealed record MentionPollResult(
    bool NotModified,
    IReadOnlyList<MentionThread> Mentions,
    string? LastModified,
    int PollIntervalSeconds);

public sealed class GitHubRestClient(HttpClient http, ILogger<GitHubRestClient> logger)
{
    private const string NotificationsUrl = "https://api.github.com/notifications?participating=true";

    public async Task<MentionPollResult> FetchMentionsAsync(
        string token, string? ifModifiedSince, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, NotificationsUrl);
        ApplyHeaders(request, token);
        if (ifModifiedSince is not null && DateTimeOffset.TryParse(ifModifiedSince, out var since))
            request.Headers.IfModifiedSince = since;

        using var response = await http.SendAsync(request, cancellationToken);
        var pollInterval = ReadPollInterval(response);

        if (response.StatusCode == HttpStatusCode.NotModified)
            return new MentionPollResult(true, [], ifModifiedSince, pollInterval);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new GitHubAuthException("GitHub retornou 401 na API de notificações.");

        response.EnsureSuccessStatusCode();

        var lastModified = response.Content.Headers.LastModified?.ToString("R") ?? ifModifiedSince;
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var mentions = new List<MentionThread>();
        foreach (var thread in document.RootElement.EnumerateArray())
        {
            if (GetString(thread, "reason") != "mention")
                continue;

            if (thread.TryGetProperty("subject", out var subject) is false
                || GetString(subject, "type") != "PullRequest")
                continue;

            var subjectUrl = GetString(subject, "url");
            if (subjectUrl is null || !TryParsePrNumber(subjectUrl, out var prNumber))
                continue;

            var repo = thread.TryGetProperty("repository", out var repository)
                ? GetString(repository, "full_name")
                : null;
            if (repo is null)
                continue;

            mentions.Add(new MentionThread(
                GetString(thread, "id") ?? "",
                repo,
                prNumber,
                GetString(subject, "title") ?? "",
                GetString(thread, "updated_at") ?? "",
                GetString(subject, "latest_comment_url")));
        }

        return new MentionPollResult(false, mentions, lastModified, pollInterval);
    }

    public async Task<MentionDetails?> GetMentionDetailsAsync(
        string token, string commentApiUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, commentApiUrl);
            ApplyHeaders(request, token);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            var root = document.RootElement;
            var author = root.TryGetProperty("user", out var user) ? GetString(user, "login") : null;
            return new MentionDetails(GetString(root, "html_url"), author, GetString(root, "body"));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha ao buscar detalhes do comentário de menção em {Url}.", commentApiUrl);
            return null;
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    private static int ReadPollInterval(HttpResponseMessage response)
        => response.Headers.TryGetValues("X-Poll-Interval", out var values)
           && int.TryParse(values.FirstOrDefault(), out var seconds)
            ? Math.Max(seconds, 60)
            : 60;

    private static bool TryParsePrNumber(string subjectUrl, out int number)
        => int.TryParse(subjectUrl[(subjectUrl.LastIndexOf('/') + 1)..], out number);

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
