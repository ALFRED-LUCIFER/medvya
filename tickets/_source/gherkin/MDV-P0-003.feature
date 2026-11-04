Feature: Decide launch market (ADR-002) and confirm Azure subscription and region
  As a founder, I want the launch market and Azure subscription decided, so that infrastructure and sequencing can proceed.

  # chunk: Record launch market, Azure subscription and region, credits and AI availability
  @todo
  Scenario: Launch market decision is accepted
    Then ADR-002 is Accepted with the chosen market and the reasoning

  @todo
  Scenario: Azure subscription details are recorded without secrets
    Then docs/03-architecture/azure-deployment.md records the subscription ID, tenant, billing owner and launch region
    And it contains no secrets

  @todo
  Scenario: Startup credit application is settled
    Then the Microsoft for Startups (Founders Hub) credit application is submitted or explicitly declined

  @todo
  Scenario: AI service availability is confirmed in the launch region
    Then availability of Azure OpenAI (chat model) and AI Speech in the launch region is confirmed with a dated note
    And AI Speech covers batch transcription and diarisation for en-IN, hi-IN, ta-IN or ar-AE

  @todo
  Scenario: UAE choice acknowledges sequencing impact
    Given the UAE is chosen as the launch market
    Then the ADR acknowledges that the EID reader and exchange are on the critical path to week 29
