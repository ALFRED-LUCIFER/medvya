Feature: Referrals out and in with status (E-09)
  As a doctor, I want to send and track referrals, so that patients are not lost between providers.

  # chunk: Add referral sending with consent and letter
  @todo
  Scenario: Consent and channel recorded
    Given a referral where policy demands sharing consent
    When the doctor sends it
    Then consent is required and channel and time are recorded
  @todo
  Scenario: Letter contains only selected content
    Given a referral letter PDF
    When generated
    Then it includes only the selected summary content

  # chunk: Add referral status tracking
  @todo
  Scenario: Status updates with evidence
    Given an open referral
    When the status moves, e.g. report received
    Then it can carry evidence such as a document link
  @todo
  Scenario: Open referrals on Patient 360
    Given open referrals
    When Patient 360 is viewed
    Then they are visible
