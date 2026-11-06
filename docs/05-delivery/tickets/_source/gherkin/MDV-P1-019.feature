Feature: Legal terms and separate versioned consents at registration (treatment, reminders per channel, sharing, portal, marketing)
  As a receptionist, I want to record each patient consent separately, so that every decision is lawful and traceable.

  # chunk: Add separate consent toggles at registration
  @done
  Scenario: Consents are separate toggles with safe defaults
    When the Consents step is shown
    Then every consent is a separate toggle
    And marketing defaults off
    And treatment is required and cannot be toggled off

  # chunk: Add immutable consent receipts
  @done
  Scenario: Each decision creates an immutable receipt
    When a consent decision is made
    Then an immutable receipt is stored with policy version and jurisdiction
    And changing a decision creates a new receipt and keeps history

  @todo
  Scenario: Privacy notice version is the published one
    Given a new notice version is published
    Then registration shows the currently published version
    And old receipts are not altered

  # chunk: Gate tenant activation and sending on consent
  @todo
  Scenario: Production tenant needs an accepted-terms record
    Given no accepted-terms record exists for the production tenant
    When it tries to book an appointment or send a message
    Then the request is refused

  @todo
  Scenario: Accepting the terms activates the tenant
    When an owner accepts the ToS and DPA
    Then one record stores the terms version, the time and who accepted
    And booking and sending are allowed for that tenant

  @done
  Scenario: Reminders check channel consent before every send
    When a reminder is about to be sent
    Then the channel consent is checked first (test)

  # chunk: Load legally reviewed wording
  @todo
  Scenario: Final wording is the reviewed text in three languages
    Then the consent texts are the legally reviewed text from MDV-P1-044 in en, hi and ar
