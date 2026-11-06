Feature: Platform: public FHIR APIs, native mobile apps (Expo), saved migration templates per vendor, specialty templates
  As a developer or partner, I want platform capabilities, so that others can integrate and use Medvya on mobile.

  # chunk: Require an intake record for each platform item
  @todo
  Scenario: Each item passes its own intake record first
    Given public FHIR APIs, native apps, migration templates and specialty templates
    When work on one item is about to start
    Then that item has its own approved intake record

  # chunk: Publish FHIR APIs with SMART-on-FHIR auth
  @todo
  Scenario: Public APIs enforce tenant, relationship and purpose authorisation
    Given a partner calling a public FHIR API with SMART-on-FHIR auth
    When the request lacks tenant, care relationship or purpose rights
    Then it is denied

  @todo
  Scenario: Public API calls are audited
    Given an authorised public FHIR API call
    When it completes
    Then it is audited like any other access

  # chunk: Build native apps with shared contracts and i18n
  @todo
  Scenario: Native app shares contracts and i18n
    Given the Expo native app
    When it is built
    Then it uses the shared contracts and the shared translations

  # chunk: Add migration and specialty templates
  @todo
  Scenario: Saved migration templates per legacy vendor and specialty templates
    Given a legacy vendor with a saved migration template and a specialty note template
    When an admin selects them
    Then the saved migration template is available per vendor and the specialty template is available for notes
