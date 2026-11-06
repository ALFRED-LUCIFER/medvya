Feature: Medication reconciliation: chart vs patient-reported vs other source, keep / change / stop (E-07)
  As a doctor, I want to reconcile medicines across sources, so that the prescription starts from a decided and correct list.

  # chunk: Add the reconciliation table and confirm gate
  @todo
  Scenario: Confirm disabled until all decided
    Given rows without a decision
    When the doctor tries "Confirm changes" in the UI or API
    Then it is refused until every row is decided
  @todo
  Scenario: Conflicts flagged in text
    Given the chart says atorvastatin is active and the patient says stopped
    When the table is shown
    Then the conflict is flagged with text such as "atorvastatin active in chart, patient says stopped"
  @todo
  Scenario: Decisions are recorded
    Given a Stop or Change decision
    When confirmed
    Then actor, time and reason are recorded and history is kept
  @todo
  Scenario: Stopped medicines are visible
    Given a stopped medicine
    When Patient 360 and the timeline are viewed
    Then it shows as stopped with its date
