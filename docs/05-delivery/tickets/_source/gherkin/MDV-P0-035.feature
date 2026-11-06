Feature: National IDs unreadable to database admins: envelope encryption with Key Vault keys and an HMAC blind index
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: National IDs unreadable to database admins: envelope encryption with K
  @todo
  Scenario: Check 1
    Given a database admin
    When reads the column with raw SQL
    Then only ciphertext is visible
  @todo
  Scenario: Check 2
    Given a lookup by national ID
    When it runs
    Then it uses the blind index and finds exactly one patient
  @todo
  Scenario: Check 3
    Given a key is rotated
    When the job runs
    Then data keys are re-wrapped and existing rows still decrypt
  @todo
  Scenario: Check 4
    Given Key Vault
    When is unreachable
    Then the app fails closed and logs no identifier
