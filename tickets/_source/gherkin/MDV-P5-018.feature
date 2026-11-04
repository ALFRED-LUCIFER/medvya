Feature: In-app role guides, help centre, feedback and public status page (N-13)
  As any user, I want role guides, help and feedback, so that I can learn and report problems.

  # chunk: Add role first-run guides
  @todo
  Scenario: First-run guide
    Given each role
    When first used
    Then a guide covers its core loop and can be dismissed and replayed

  # chunk: Add feedback and status page
  @todo
  Scenario: Feedback ticket without PHI
    Given in-app feedback
    When submitted
    Then a ticket is created with tenant id but no PHI unless deliberately attached (warning shown)
  @todo
  Scenario: Status page availability
    Given the status page
    When viewed
    Then it reports monthly availability against the 99.5 % target and incident history

  # chunk: Provide role training packs
  @todo
  Scenario: Training materials
    Given the delivery package
    When checked
    Then 30-minute role training materials exist
