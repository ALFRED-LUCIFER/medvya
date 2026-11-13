Feature: The note always shows what the server holds; the last keystrokes survive a page change and a reload
  Type in Assessment, open Problems within two seconds, come back: the field shows old or empty text although the server has the new text.

  # chunk: The note always shows what the server holds; the last keystrokes survive a page change and a reload
  @todo
  Scenario: A page hop shows the new text
    Given text typed in Assessment
    When the doctor opens Problems within 500 ms and comes back
    Then the field shows the new text

  @todo
  Scenario: A reload keeps the last words
    Given text typed in Plan
    When the page is reloaded within 500 ms
    Then the new text is there

  @todo
  Scenario: Saved means the server answered
    Given a slow or failed save
    When the status is read
    Then it says Saving… or Not saved. Trying again. and never Saved

  @todo
  Scenario: The stale-text guard is proven
    Given the page-hop test
    When the refetch on mount is removed
    Then the test fails

  @todo
  Scenario: Conflicts still detected
    Given the same note open in two tabs
    When both type
    Then the second writer is told and keeps their text, as today

  @todo
  Scenario: Nothing in browser storage
    Given a consultation in use
    When localStorage, sessionStorage and IndexedDB are read
    Then they hold no note text

  @todo
  Scenario: Announced
    Given a screen reader
    When the status changes
    Then it is announced politely, and axe finds no violation in English or Arabic
