Feature: Clinic messages inbox (patient ↔ clinic) with AI reply draft approved by staff (C-06)
  As a receptionist, I want a messages inbox with AI reply drafts I approve, so that patients get timely safe replies.

  # chunk: Add messages inbox with SLA and encryption
  @todo
  Scenario: SLA breach shown
    Given a thread past its SLA
    When the thread or notifications are viewed
    Then the SLA breach shows on both
  @todo
  Scenario: No content in push or logs
    Given a message
    When push notifications and logs are inspected
    Then the message content never appears

  # chunk: Add AI draft approval and clinical routing
  @todo
  Scenario: AI draft needs approval
    Given an AI-drafted reply
    When staff have not explicitly approved it
    Then it is not sent
  @todo
  Scenario: Clinical questions routed
    Given a flagged clinical question
    When a non-clinician tries to send clinical advice
    Then the role check on send refuses it
    And the thread is routed to the clinician
