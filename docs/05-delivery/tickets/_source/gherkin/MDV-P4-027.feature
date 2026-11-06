Feature: Public doctor and clinic profiles with live availability, indexed for search (N-02, design needed)
  As a member of the public, I want to find a doctor's profile with live availability, so that I can book.

  # chunk: Approve the N-02 design
  @todo
  Scenario: Design approved first
    Given the N-02 spike
    When implementation is about to start
    Then a design is produced and approved

  # chunk: Add opt-in public profiles with availability
  @todo
  Scenario: Opt-in and no patient data
    Given a doctor or clinic profile
    When it is public
    Then it is opt-in and shows no patient data
    And availability shows slot times only

  # chunk: Add search indexing and booking entry
  @todo
  Scenario: Indexable and fast
    Given the public pages
    When checked
    Then they are in the sitemap with schema.org Physician/MedicalClinic markup
    And Lighthouse is ≥ 90
  @todo
  Scenario: Booking leads to portal
    Given a profile
    When the visitor chooses Book
    Then they enter portal sign-in and booking
