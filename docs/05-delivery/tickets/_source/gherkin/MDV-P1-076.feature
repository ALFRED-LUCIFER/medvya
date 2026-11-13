Feature: Guardrails: lint rules and a CI count that may only fall, so shared patterns stop drifting
  A rule that is only written drifts again.

  # chunk: Guardrails: lint rules and a CI count that may only fall, so shared patterns stop drifting
  @todo
  Scenario: A raised count fails the build
    Given the committed baseline
    When a pull request adds one more local Message component or one more h-11 patch in a feature
    Then pnpm check:ui-drift exits 1 and names the file

  @todo
  Scenario: A lowered count passes and tightens
    Given a pull request that removes patches
    When the baseline in the same pull request is lowered to the new number
    Then the check passes and the old number can no longer be reached

  @todo
  Scenario: The guard is proven
    Given the script
    When it runs with --self-test
    Then a sample with one extra hit fails and a sample at the baseline passes

  @todo
  Scenario: Runs in CI
    Given a pull request that touches web/, packages/ or the tool
    When the PR workflow runs
    Then the count step runs in the web job and a failure blocks the merge

  @todo
  Scenario: No guard is weakened
    Given web/clinic/.oxlintrc.json and the workflow
    When this change is compared with main
    Then no existing rule, test or threshold is removed or lowered

  @todo
  Scenario: Baseline is honest
    Given the baseline file
    When the counts are re-measured on main at merge time
    Then each number equals the measured number
