import { getJson, post } from '../../api/services/api';
import type { PlanVersion } from '../domain/plans';

export const fetchPlans = () => getJson<PlanVersion[]>('/v1/plans');

export const saveVersion = async (code: string, body: unknown) =>
  (await post(`/v1/plans/${encodeURIComponent(code)}/versions`, body)).json() as Promise<PlanVersion>;
