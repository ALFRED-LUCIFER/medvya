Feature: Spike: Azure AI Speech and Azure OpenAI in-region capability check and ADR for speech/LLM providers
  As a developer, I want to confirm in-region speech and LLM capability, so that the AI pipeline is built on proven providers.

  # chunk: Run the in-region spike and record ADR-007
  @todo
  Scenario: ADR-007 accepted
    Given the spike results
    When ADR-007 is reviewed
    Then it is accepted with model names/versions, regions, quotas and a fallback provider abstraction
  @todo
  Scenario: Latency vs 60 s target
    Given 10 synthetic recordings run through the pipeline
    When latency is measured
    Then it supports the 60 s p50 draft target or the gap is quantified with a plan
  @todo
  Scenario: Quality per language recorded
    Given the synthetic set
    When WER and diarisation error are measured
    Then quality per language is recorded
  @todo
  Scenario: No data leaves the country
    Given inference in the launch region
    When the terms are checked
    Then it is documented with Microsoft terms that no data leaves the country
