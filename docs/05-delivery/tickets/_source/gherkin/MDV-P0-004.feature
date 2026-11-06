Feature: GitHub repository protections and PR pipeline (build, analyzers, tests, CodeQL, dependency and secret scanning)
  As a developer, I want every pull request checked and main protected, so that nothing broken or leaking reaches main.

  # chunk: Add PR pipeline, branch protection, Dependabot and PR template
  @done
  Scenario Outline: A failing check blocks merge into main
    Given a pull request whose <check> fails
    When I try to merge it into main
    Then the merge is blocked

    Examples:
      | check          |
      | build          |
      | tests          |
      | lint           |
      | type-check     |
      | CodeQL         |
      | secret scanning |

  @done
  Scenario: Direct and force pushes to main are rejected
    When I push or force-push directly to main
    Then the push is rejected

  @done
  Scenario: Dependabot opens weekly update PRs
    Then Dependabot opens update PRs for NuGet, npm and GitHub Actions weekly

  @done
  Scenario: PR template carries the checklist
    Then the PR template contains the X1-X8 Definition-of-Done checklist
    And it contains a "synthetic data only" confirmation

  @done
  Scenario: Pipeline is fast on the skeleton
    When the pipeline runs on the skeleton
    Then it finishes in under 10 minutes
