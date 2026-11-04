Feature: Solo doctor and clinic sign-up with the six-step onboarding wizard (autosave)
  As a solo doctor or practice owner, I want a guided six-step onboarding that saves my progress, so that I can set up my practice and resume at any time.

  # chunk: Add per-country validation to onboarding steps
  @todo
  Scenario Outline: Fields validate per country
    Given a user is in the onboarding wizard for <country>
    When they enter <field> incorrectly
    Then an error is shown under the field and announced via aria-live
    Examples:
      | country | field                                   |
      | India   | council or registration number          |
      | India   | GSTIN optional format                   |
      | UAE     | DoH, DHA or MOHAP licence               |
      | UAE     | TRN not 15 digits                       |

  # chunk: Add autosave and resume to the wizard
  @todo
  Scenario: Leaving mid-wizard resumes at the same step
    Given a user has filled part of the wizard and left
    When they sign in again
    Then the wizard resumes at the same step with saved values

  # chunk: Add unverified licence restrictions
  @todo
  Scenario: Doctor with unverified licence can book but not record or sign
    Given a doctor's licence is not yet verified
    Then the doctor can book and register patients
    And recording and signing are disabled with the designed notice
    And the restriction is enforced by a feature-level check, not just the UI

  # chunk: Add team invitations and finish step
  @todo
  Scenario: Step 6 sends invitations through the P0 invitation flow
    When the user invites team members in step 6
    Then invitations are sent through the MDV-P0-013 invitation flow

  @todo
  Scenario Outline: Finishing lands on the role's home and creates the practice
    When a <role> finishes the wizard
    Then they land on <home>
    And the tenant, facility, membership, practice profile, hours and fee defaults are created
    Examples:
      | role               | home       |
      | doctor             | Today      |
      | owner or reception | Front desk |

  # chunk: Make wizard tablet and keyboard friendly
  @todo
  Scenario: Wizard works at tablet width and by keyboard
    Given a viewport of 768 px
    When a user completes every step using only the keyboard
    Then the whole wizard works
