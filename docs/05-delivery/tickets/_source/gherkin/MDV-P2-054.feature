Feature: Tables: one controller for sort and paging, stacked rows on phones, and compact density for four reference tables only
  Staff rows are about 69 px (7 per screen); the audit table starts 800 px down a 4,192 px page; 2 of 19 tables can be sorted; 10 rely on sideways scrolling on phones, which can hide dose or identity columns.

  # chunk: Controller, sort and paging on the audit log and staff tables
  @todo
  Scenario: Compact only where allowed
    Given the density prop
    When it is set on a table other than audit log, formulary, order sets or staff
    Then the build or lint fails

  @todo
  Scenario: Never compact on touch
    Given a compact table on a device with a coarse pointer
    When it renders
    Then rows are 48 px

  @todo
  Scenario: More rows for reference work
    Given the audit log at 1440 by 900 with a mouse
    When it opens
    Then at least 15 rows are visible and the table starts in the first 300 px

  # chunk: Stacked rows under 768 px for every table
  @todo
  Scenario: Sort and paging are shared
    Given staff and audit log
    When a sortable header is used by keyboard
    Then the order changes, the sort state is announced, and lists over 50 rows are paged

  @todo
  Scenario: Phones get stacked rows
    Given any table at 375 px
    When it renders
    Then there is no sideways scrolling and no dose or identity field is hidden

  # chunk: Compact density for the four reference tables, with the rule amended
  @todo
  Scenario: Destructive actions stay large
    Given Offboard in the compact staff table
    When it is measured
    Then it is at least 44 px or opens a confirmation

  @todo
  Scenario: House rule amended
    Given AGENTS.md section 4
    When this change merges
    Then it names the four tables and the coarse-pointer rule

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs on each migrated table
    Then there is no violation and every scrollable region has a label
