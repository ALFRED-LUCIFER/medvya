Feature: Licence/registration expiry reminders and quarterly access review prompt
  As a practice owner, I want reminders before licences expire and a quarterly access review, so that compliance lapses are caught early.

  # chunk: Add licence expiry notifications and enforcement
  @todo
  Scenario Outline: Expiry notifications are sent
    Given a doctor licence or facility registration expires
    When it is <days> days before expiry
    Then a notification is sent by email and appears in the notification centre (D-03)
    Examples:
      | days |
      | 60   |
      | 30   |
      | 7    |
      | 0    |

  @todo
  Scenario: Expired licence disables capabilities server-side
    Given a licence has expired
    Then capabilities requiring a verified licence are disabled server-side
    And they stay disabled until renewed and re-verified

  # chunk: Add quarterly access review and idempotent audited jobs
  @todo
  Scenario: Quarterly review records a decision per person
    When the owner opens the quarterly access review
    Then all memberships are listed
    And a decision is recorded per person
    And the completion date is shown in settings

  @todo
  Scenario: Jobs are idempotent and audited
    When either job runs more than once for the same period
    Then no duplicate effects occur
    And each run is audited
