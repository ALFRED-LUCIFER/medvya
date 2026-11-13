Feature: GitHub OIDC federation to Azure and tagged deploy to dev
  As a developer, I want GitHub Actions to deploy to Azure without stored secrets, so that releases are safe and repeatable.

  # chunk: Federate GitHub to Azure without stored secrets
  @todo
  Scenario: No Azure client secret or publish profile in GitHub
    Then no Azure client secret or publish profile exists in GitHub secrets
    And only federated credentials are used

  # chunk: Deploy a tag on main to dev
  @todo
  Scenario: Tag on main deploys to dev
    When I push tag v0.0.1 on main
    Then dev deploys end-to-end
    And the health endpoint returns 200 with the tag version

  @todo
  Scenario: Tag on a non-main commit is rejected
    When I push a tag on a commit that is not on main
    Then the workflow rejects it

  # chunk: Run migrations with the migration user and make deploys idempotent
  @todo
  Scenario: Migrations use the migration database user
    When a deploy runs migrations
    Then they run with the dedicated migration database user, not the app identity

  @todo
  Scenario: Re-running the same tag is idempotent
    When I re-run the deploy for the same tag
    Then the result is the same and nothing breaks
