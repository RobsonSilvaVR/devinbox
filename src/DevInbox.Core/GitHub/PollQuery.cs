namespace DevInbox.Core.GitHub;

public static class PollQuery
{
    public const string ViewerQuery = "query { viewer { login } }";

    public const string Text = """
        query Poll($qMine: String!, $qReview: String!) {
          viewer { login }
          rateLimit { cost remaining resetAt }
          mine: search(query: $qMine, type: ISSUE, first: 25) {
            nodes {
              ... on PullRequest {
                id number title url isDraft
                repository { nameWithOwner }
                mergeable
                comments(last: 15) {
                  nodes { id url bodyText createdAt author { login } }
                }
                reviews(last: 10) {
                  nodes { id url state bodyText submittedAt author { login } }
                }
                reviewThreads(first: 20) {
                  nodes {
                    id isResolved
                    comments(last: 5) {
                      nodes { id url bodyText createdAt viewerDidAuthor author { login } }
                    }
                  }
                }
                commits(last: 1) {
                  nodes {
                    commit {
                      oid
                      statusCheckRollup {
                        state
                        contexts(last: 20) {
                          nodes {
                            __typename
                            ... on CheckRun { name conclusion detailsUrl }
                            ... on StatusContext { context state targetUrl }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
          reviewRequested: search(query: $qReview, type: ISSUE, first: 25) {
            nodes {
              ... on PullRequest {
                id number title url
                repository { nameWithOwner }
                author { login }
              }
            }
          }
        }
        """;

    public static object BuildVariables(string login) => new
    {
        qMine = $"is:pr is:open author:{login} archived:false sort:updated-desc",
        qReview = $"is:pr is:open review-requested:{login} archived:false sort:updated-desc",
    };
}
