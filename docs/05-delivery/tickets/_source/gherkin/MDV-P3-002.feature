Feature: Consent templates and recording policy administration (H-09)
  As a practice admin, I want versioned consent templates and a recording policy, so that consent is consistent and traceable.

  # chunk: Add versioned consent templates
  @todo
  Scenario: Draft, preview, publish immutable
    Given a template per language
    When the admin drafts, previews and publishes a version
    Then the published version is immutable
  @todo
  Scenario: Receipts reference exact version
    Given a consent receipt
    When it is read
    Then it references the exact template version and language shown
  @todo
  Scenario: Recording script from published template
    Given the patient's language
    When the recording consent script is served
    Then it comes from the published template

  # chunk: Add recording policy administration
  @todo
  Scenario: Policy editable by admins only and audited
    Given the recording policy (retention days default 30, purpose "documentation only")
    When edited
    Then only admins can edit it and the change is audited
