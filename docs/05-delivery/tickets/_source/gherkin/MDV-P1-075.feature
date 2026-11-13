Feature: Design rules: write PRODUCT.md and DESIGN.md from the audit blueprint and amend AGENTS.md section 4
  There is no written rule for density, page headers, card sizes, how many primary buttons a view may have, or which shared component a pattern must use.

  # chunk: Design rules: write PRODUCT.md and DESIGN.md from the audit blueprint and amend AGENTS.md section 4
  @todo
  Scenario: Both briefs exist
    Given the repository root
    When PRODUCT.md and DESIGN.md are opened
    Then each section of blueprint sections 1 to 14 is present in one of them and docs/README.md and AGENTS.md section 8 link to both

  @todo
  Scenario: Rulings are binding text
    Given DESIGN.md
    When the density, phone, navigation rail and owner page rules are read
    Then they match arbitration rulings 2, 5, 8 and 7 word for word in meaning and name the four tables that may be compact

  @todo
  Scenario: House rules amended in the same change
    Given AGENTS.md section 4
    When it is read after this change
    Then it states the 44 px library default, the two densities, 16 px typed clinical text, the 1024 tier, the shared components to use and the motion rules, and each rule the code does not yet meet names the ticket that makes it true

  @todo
  Scenario: Safety lists are written down
    Given DESIGN.md
    When the list headed Do not change without clinical validation is read
    Then it holds every item of blueprint section 14 and of the executive verdict, and the epic's seven non-negotiables

  @todo
  Scenario: Accessibility targets are stated as targets
    Given DESIGN.md
    When the accessibility section is read
    Then it lists the WCAG 2.2 criteria of blueprint section 13 as design requirements and makes no claim of conformance

  @todo
  Scenario: Links resolve
    Given the changed Markdown files
    When python3 -I tools/check-links/check.py runs
    Then it reports 0 broken links
