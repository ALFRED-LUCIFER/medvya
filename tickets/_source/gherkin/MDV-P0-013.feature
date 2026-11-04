Feature: Invite a colleague and accept the invitation (membership invited -> active)
  As a practice admin, I want to invite a colleague with a role, so that they can join the clinic safely.

  # chunk: Issue single-use invitation tokens and accept them
  @done
  Scenario: Invitation tokens are secure
    Then invitation tokens are single-use and random (at least 128 bits)
    And they expire after the configured period (default 7 days)
    And they are stored hashed

  @done
  Scenario: Acceptance screen shows role, scope and capabilities
    When an invitee opens the invitation link
    Then the screen shows role, facility scope and capability list exactly as derived from the role-permission matrix

  @done
  Scenario: Acceptance activates the membership
    When the invitee accepts
    Then the membership is active
    And the user lands on the role's home route

  @done
  Scenario Outline: Unusable token shows the expired state
    Given a token that is <state>
    When it is opened
    Then the expired state is shown
    And no membership is created

    Examples:
      | state        |
      | expired      |
      | revoked      |
      | already used |

  @done
  Scenario: Invites and acceptances are audited and email has no patient data
    Then invites and acceptances are audited
    And the invite email contains no patient data

  # chunk: Add SMS invitations and clinician licence flag
  @todo
  Scenario: Invite SMS contains no patient data
    When an invitation is sent by SMS
    Then it contains no patient data

  @todo
  Scenario: Clinician invites are flagged for licence verification
    When a clinician is invited
    Then the invite is flagged "licence verification required"
