namespace Symphony.Linear;

public static class LinearQueries
{
    public const int IssuePageSize = 50;

    public const string CandidateIssues = """
        query SymphonyLinearPoll($projectSlug: String!, $stateNames: [String!]!, $first: Int!, $relationFirst: Int!, $after: String) {
          issues(filter: {project: {slugId: {eq: $projectSlug}}, state: {name: {in: $stateNames}}}, first: $first, after: $after) {
            nodes {
              id
              identifier
              title
              description
              priority
              state {
                name
              }
              branchName
              url
              assignee {
                id
              }
              labels {
                nodes {
                  name
                }
              }
              comments(first: 50) {
                nodes {
                  id
                  body
                  createdAt
                  updatedAt
                }
              }
              attachments(first: 25) {
                nodes {
                  id
                  title
                  url
                  sourceType
                }
              }
              inverseRelations(first: $relationFirst) {
                nodes {
                  type
                  issue {
                    id
                    identifier
                    state {
                      name
                    }
                  }
                }
              }
              createdAt
              updatedAt
            }
            pageInfo {
              hasNextPage
              endCursor
            }
          }
        }
        """;

    public const string IssuesByIds = """
        query SymphonyLinearIssuesById($ids: [ID!]!, $first: Int!, $relationFirst: Int!) {
          issues(filter: {id: {in: $ids}}, first: $first) {
            nodes {
              id
              identifier
              title
              description
              priority
              state {
                name
              }
              branchName
              url
              assignee {
                id
              }
              labels {
                nodes {
                  name
                }
              }
              comments(first: 50) {
                nodes {
                  id
                  body
                  createdAt
                  updatedAt
                }
              }
              attachments(first: 25) {
                nodes {
                  id
                  title
                  url
                  sourceType
                }
              }
              inverseRelations(first: $relationFirst) {
                nodes {
                  type
                  issue {
                    id
                    identifier
                    state {
                      name
                    }
                  }
                }
              }
              createdAt
              updatedAt
            }
          }
        }
        """;

    public const string Viewer = """
        query SymphonyLinearViewer {
          viewer {
            id
          }
        }
        """;

    public const string ProjectWorkflowStates = """
        query SymphonyProjectWorkflowStates($projectSlug: String!, $first: Int!) {
          projects(filter: {slugId: {eq: $projectSlug}}, first: 1) {
            nodes {
              teams {
                nodes {
                  states(first: $first) {
                    nodes {
                      name
                      type
                    }
                  }
                }
              }
            }
          }
        }
        """;

    public const string CreateComment = """
        mutation SymphonyCreateComment($issueId: String!, $body: String!) {
          commentCreate(input: {issueId: $issueId, body: $body}) {
            success
          }
        }
        """;

    public const string UpdateState = """
        mutation SymphonyUpdateIssueState($issueId: String!, $stateId: String!) {
          issueUpdate(id: $issueId, input: {stateId: $stateId}) {
            success
          }
        }
        """;

    public const string ResolveStateId = """
        query SymphonyResolveStateId($issueId: String!, $stateName: String!) {
          issue(id: $issueId) {
            team {
              states(filter: {name: {eq: $stateName}}, first: 1) {
                nodes {
                  id
                }
              }
            }
          }
        }
        """;
}

