Feature: Releases and What's new, maintenance and incident banners, integrations register
  As medvya operations, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Releases and What's new, maintenance and incident banners, integration
  @todo
  Scenario: Check 1
    Given a flag reaches a tenant
    When an announcement is linked
    Then it shows in What's new only then
  @todo
  Scenario: Check 2
    Given maintenance is scheduled for India
    When the time comes
    Then only India practices see the banner
  @todo
  Scenario: Check 3
    Given the register
    When is opened
    Then it shows each integration's status and country
