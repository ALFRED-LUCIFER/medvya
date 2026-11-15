from datetime import date
from typing import Annotated, Literal, get_args

from fastapi import APIRouter, Query

from medvya_ai.deps import TenantDep
from medvya_ai.errors import AppError
from medvya_ai.models.base import ENUMS
from medvya_ai.schemas import FactCountsOut, MedicationUsageOut, OverviewOut, UsageOut
from medvya_ai.services import analytics as svc

router = APIRouter(prefix="/analytics", tags=["analytics"])

Category = Literal[
    "symptom",
    "diagnosis",
    "medication",
    "allergy",
    "vital",
    "exam_finding",
    "investigation",
    "follow_up",
    "advice",
    "history",
]
assert set(get_args(Category)) == set(ENUMS["fact_category"])  # noqa: S101 - import-time drift guard

From = Annotated[date | None, Query(alias="from")]
To = Annotated[date | None, Query(alias="to")]
Limit = Annotated[int, Query(ge=1, le=200)]


def _check(d_from: date | None, d_to: date | None) -> None:
    if d_from and d_to and d_from > d_to:
        raise AppError(422, "VALIDATION_ERROR", "from must not be after to")


@router.get("/medications", response_model=MedicationUsageOut)
async def medications(
    t: TenantDep,
    d_from: From = None,
    d_to: To = None,
    group: svc.Group = "day",
    limit: Limit = 20,
    include_draft: bool = False,
) -> MedicationUsageOut:
    _check(d_from, d_to)
    rows = await svc.medications(
        t.db,
        t.clinic_id,
        group=group,
        d_from=d_from,
        d_to=d_to,
        limit=limit,
        include_draft=include_draft,
    )
    return MedicationUsageOut.model_validate({"items": rows})


@router.get("/facts", response_model=FactCountsOut)
async def facts(
    t: TenantDep,
    category: Category | None = None,
    d_from: From = None,
    d_to: To = None,
    limit: Limit = 20,
) -> FactCountsOut:
    _check(d_from, d_to)
    rows = await svc.facts(
        t.db, t.clinic_id, category=category, d_from=d_from, d_to=d_to, limit=limit
    )
    return FactCountsOut.model_validate({"items": rows})


@router.get("/usage", response_model=UsageOut)
async def usage(t: TenantDep, d_from: From = None, d_to: To = None) -> UsageOut:
    _check(d_from, d_to)
    return UsageOut.model_validate(
        {"items": await svc.usage(t.db, t.clinic_id, d_from=d_from, d_to=d_to)}
    )


@router.get("/overview", response_model=OverviewOut)
async def overview(t: TenantDep, d_from: From = None, d_to: To = None) -> OverviewOut:
    _check(d_from, d_to)
    return OverviewOut.model_validate(
        await svc.overview(t.db, t.clinic_id, d_from=d_from, d_to=d_to)
    )
