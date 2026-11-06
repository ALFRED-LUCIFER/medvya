import { theme } from '@medvya/tokens';
import { createContext, useContext } from 'react';

/** What differs between the two apps: the accent (teal clinic, indigo patient) and the patient app's larger text and targets. */
export type Brand = { solid: string; tint: string; target: number; body: number };

export const clinicBrand: Brand = { solid: theme.color.brand700, tint: theme.color.brand50, target: 44, body: 15 };
// Patient shell: 17 px minimum text and 48 px targets (docs/00-overview/personas.md).
export const patientBrand: Brand = { solid: theme.color.patient700, tint: theme.color.patient50, target: 48, body: 17 };

const BrandContext = createContext<Brand>(clinicBrand);
export const BrandProvider = BrandContext.Provider;
export const useBrand = () => useContext(BrandContext);
