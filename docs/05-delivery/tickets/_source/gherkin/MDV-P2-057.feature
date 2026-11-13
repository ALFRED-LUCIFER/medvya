Feature: Apply the type and width tokens: stray font sizes removed, body weight 500, page maximum 1,600 px, reading width, no sm:grid-cols
  12 rendered font sizes against a scale of 7; bold used 250 times and medium once, so weight ranks nothing; paragraphs up to 1,160 px wide; settings inputs 1,100 px; the sm: step is 360 px, so small-screen column rules apply on phones and clip the registration stepper at 375.

  # chunk: Apply the type and width tokens: stray font sizes removed, body weight 500, page maximum 1,600 px, reading width, no sm:grid-cols
  @todo
  Scenario: Nine sizes only
    Given the 20 most-used screens at 1440
    When rendered font sizes are measured
    Then only the nine token sizes appear (the queue display and print excepted)

  @todo
  Scenario: Weight ranks
    Given a list row
    When it renders
    Then the identifying cell is bold and body text is weight 500

  @todo
  Scenario: Lines are readable
    Given 1920 px
    When Today, the patient record and Settings render
    Then no paragraph is wider than about 72 characters and the page content is at most 1,600 px wide

  @todo
  Scenario: Phones are not cramped
    Given Register patient at 375 px
    When the stepper renders
    Then no step label is clipped

  @todo
  Scenario: Rules now block it
    Given a feature file
    When text-[13.5px] or sm:grid-cols-2 is added
    Then lint fails

  @todo
  Scenario: Nothing lost; languages and axe
    Given captures before and after in English and Arabic
    When compared
    Then no text is truncated or overlapping, and axe finds no violation
