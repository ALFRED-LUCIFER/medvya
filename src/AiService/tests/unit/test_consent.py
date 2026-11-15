import uuid

import pytest

from medvya_ai.services.consent import Decision, decide

A, B = uuid.uuid4(), uuid.uuid4()


@pytest.mark.parametrize(
    ("participants", "latest", "expected"),
    [
        ([], {}, Decision.REQUIRED),
        ([A, B], {}, Decision.REQUIRED),
        ([A, B], {A: "granted"}, Decision.REQUIRED),  # participant added later
        ([A, B], {A: "granted", B: "refused"}, Decision.REQUIRED),
        ([A, B], {A: "granted", B: "granted"}, Decision.ALLOWED),
        ([A, B], {A: "granted", B: "withdrawn"}, Decision.WITHDRAWN),
        ([A, B], {A: "withdrawn"}, Decision.WITHDRAWN),  # withdrawal wins over missing
        ([A], {A: "withdrawn", B: "granted"}, Decision.WITHDRAWN),
        ([A], {A: "granted", B: "withdrawn"}, Decision.ALLOWED),  # non-participant ignored
    ],
)
def test_decide(participants, latest, expected) -> None:  # type: ignore[no-untyped-def]
    assert decide(participants, latest) is expected
