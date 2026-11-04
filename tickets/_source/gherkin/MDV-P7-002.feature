Feature: Hospital onboarding wizard and record authority per facility (H-08)
  As hospital IT, I want to set record authority per facility in an onboarding wizard, so that Medvya never creates a second conflicting chart.

  # chunk: Walk the onboarding wizard and set authority per facility
  @todo
  Scenario: Wizard steps lead to pilot go-live
    Given hospital IT opens H-08
    When they work through Organisation, Facilities, SSO, HIS connection, Record authority and Pilot go-live
    Then each step can be completed in order

  @todo
  Scenario Outline: Authority is set per facility and stamped on new encounters
    Given a facility whose authority is "<choice>"
    When a new encounter is created there
    Then the encounter authority_mode is stamped as "<mode>"
    And the facility status reads "<status>"

    Examples:
      | choice        | mode           | status                              |
      | Medvya EMR    | Medvya         | Medvya authoritative                |
      | Existing HIS  | HIS            | HIS authoritative · write-back on   |
      | Existing HIS (not yet connected) | HIS | HIS not connected yet           |

  # chunk: Show HIS-synced items and mixed-authority warnings
  @todo
  Scenario: HIS authority shows synced items and no second chart
    Given a facility with HIS authority
    When a clinician views the chart
    Then items show "Synced from HIS"
    And Medvya does not create a second authoritative chart

  @todo
  Scenario: Mixed authority shows banner and source badges
    Given an organisation with facilities of different authority
    When hospital IT views the setup and cross-facility views
    Then the attention banner is shown
    And cross-facility views show source badges
