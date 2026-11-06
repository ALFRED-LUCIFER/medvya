Feature: Per-tenant feature flags with owner and kill switch
  As Medvya operations, I want per-tenant flags with a kill switch, so that features can be turned off without a redeploy.

  # chunk: Add flag evaluation with owner, audit and default off
  @done
  Scenario: Turning a flag off takes effect quickly
    When a flag is turned off for a tenant
    Then the feature is disabled for that tenant within one minute without redeploy

  @done
  Scenario: Flags have owner, description and audited changes
    Then every flag has an owner and description
    And flag changes are audited

  @done
  Scenario: Disabled feature endpoints return 404
    When an API endpoint behind a disabled flag is called
    Then it returns 404, not 403

  @done
  Scenario: New flags default to off
    Then flags default to off for new features

  # chunk: Hide disabled features from navigation
  @todo
  Scenario: Disabled features are not listed in navigation
    Then features behind a disabled flag are not listed in the tenant's navigation
