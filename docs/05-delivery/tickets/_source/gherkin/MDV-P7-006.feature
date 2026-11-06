Feature: FHIR R4 HIS interface and write-back queue with conflict review and retry (H-04)
  As hospital IT or a doctor, I want a safe write-back queue to the HIS, so that signed content reaches the HIS without silent overwrites.

  # chunk: Read patient, encounter and medicines from the HIS
  @todo
  Scenario: HIS data arrives as synced copies
    Given a connected HIS
    When patient, encounter and medicines are read
    Then they appear as "Synced from HIS" copies

  # chunk: Write back only signed content with idempotency
  @todo
  Scenario: Only clinician-signed content is written back
    Given a note that is an unsigned draft and a note that is signed
    When write-back runs
    Then only the signed content is sent with an idempotency key
    And a receipt is stored

  @todo
  Scenario: Duplicate deliveries are prevented
    Given a delivery was already sent with an idempotency key
    When the same delivery is sent again
    Then the HIS does not receive a duplicate

  @todo
  Scenario: Chart shows Not yet in HIS
    Given signed content queued but not delivered
    When the clinician views the chart
    Then it shows "Not yet in HIS"

  # chunk: Route conflicts to the signing clinician
  @todo
  Scenario: Conflict is never overwritten silently
    Given the HIS holds a newer version than Medvya's signed v1
    When write-back runs
    Then the HIS is not overwritten
    And a conflict diff is routed to the signing clinician with state "Sent to clinician" (test)

  # chunk: Retry transport errors and show attempts
  @todo
  Scenario: Failed deliveries are retried by IT and visible
    Given a delivery failed from a transport error
    When hospital IT retries it
    Then the queue shows Failed, Retrying and Delivered states as they change

  @todo
  Scenario: Attempts show timestamps and codes without PHI
    Given a delivery with several attempts
    When the attempts list is opened
    Then timestamps and response codes are shown
    And no PHI appears in logs

  @todo
  Scenario: Write-back success is at least 99 percent in the pilot
    Given the hospital pilot
    When write-back results are measured
    Then success is at least 99 percent
    And every failure and conflict is visible (exit gate 2)
