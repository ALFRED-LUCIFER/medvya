import { getJson, post } from '../../api/services/api';
import type { CountryReleases } from '../domain/features';

export const fetchReleases = () => getJson<CountryReleases[]>('/v1/features');

export const setRelease = async (country: string, key: string, body: object) => {
  await post(`/v1/features/${encodeURIComponent(country)}/${encodeURIComponent(key)}`, body);
};

export const turnOffEverywhere = async (key: string, reason: string) => { await post(`/v1/features/${encodeURIComponent(key)}/off`, { reason }); };
