Feature: Duplicate scoring incl. Arabic–English name spellings, bulk merge for high confidence, desk confirmation for borderline (N-05)
  As a receptionist, I want borderline duplicates confirmed at the desk, so that records are merged safely.

  # chunk: Add duplicate scoring
  @todo
  Scenario: Seeded variants identified
    Given the synthetic set with Arabic–English variants
    When scoring runs
    Then seeded duplicates are identified with documented precision and recall

  # chunk: Add reversible merges and desk prompt
  @todo
  Scenario: Reversible merges
    Given bulk merges
    When undone
    Then they are reversible individually and in batch
  @todo
  Scenario: Desk prompt for borderline
    Given a borderline pair
    When that patient checks in
    Then a desk prompt appears
