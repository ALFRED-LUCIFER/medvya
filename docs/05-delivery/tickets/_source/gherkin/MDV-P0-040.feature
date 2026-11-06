Feature: Repository structure: Host.Api to composition only, mobile and shared packages, tickets and design into docs
  As a developer, I want a clear and enforced repository layout, so that humans and agents find and change things in one obvious place.

  # chunk: Move feature endpoints out of Host.Api into modules
  @todo
  Scenario: Host holds no features
    Given src/Host.Api
    When the architecture test runs
    Then it fails if endpoints, services or entities of a feature are present
  @todo
  Scenario: Moved endpoints unchanged
    Given each moved endpoint group
    When its existing tests run
    Then they pass unchanged and every endpoint declares a policy or is public

  # chunk: Add mobile to the workspace and the shared packages
  @todo
  Scenario: Contract drift fails CI
    Given packages/api-client or the exported OpenAPI document
    When either drifts from the API
    Then CI fails
  @todo
  Scenario: Workspace resolves
    Given pnpm-workspace.yaml
    When pnpm install runs
    Then web, mobile and packages resolve and no package imports an app

  # chunk: Move tickets and design into docs and add the link check
  @todo
  Scenario: No broken links
    Given the whole repo
    When the link checker runs
    Then there are zero broken relative links and CI runs it
  @todo
  Scenario: Root is smaller
    Given the repository root
    When listed
    Then tickets and design are gone and their content is under docs

  # chunk: CI path filters, CODEOWNERS and docs
  @todo
  Scenario: Only affected jobs run
    Given a change to one area
    When CI runs
    Then only the affected jobs run and the required checks still gate the merge
  @todo
  Scenario: Everything passes on the new tree
    Given the new tree
    When build, test, lint, e2e and the ticket build run
    Then all pass
