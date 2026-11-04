Feature: Longitudinal timeline and trends with source labels and filters (E-02)
  As a doctor, I want one chronological timeline with source labels and filters, so that I see the whole history in one place.

  # chunk: Add the timeline entries and filters
  @todo
  Scenario: Entry content
    Given a patient with encounters, labs, medicines and documents
    When the timeline is opened
    Then each entry shows date, title, subtitle, author/source and provenance tag
  @todo
  Scenario: Filters combine and persist
    Given the doctor sets several filters
    When they navigate within the session
    Then the filters stay applied
    And an empty result shows a clear empty state

  # chunk: Add trend charts and pagination
  @todo
  Scenario: Accessible trend charts
    Given HbA1c, BP or weight values
    When a trend chart is shown
    Then values with units and dates are shown
    And the same data is available as a data table for screen readers
  @todo
  Scenario: Pagination keeps position
    Given the doctor is reading the timeline
    When they choose "Load earlier…"
    Then older entries load without losing scroll position
